using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

using System.Runtime.InteropServices.WindowsRuntime;

namespace WindowsBluetooth
{
    public class BluetoothManager
    {
        private RfcommDeviceService deviceService;
        private DeviceInformationCollection deviceCollection;

        /// <summary>
        /// socket object used to communicate with the device
        /// </summary>
        private StreamSocket bluetoothSocket;

        /// <summary>
        /// Datawriter created from the socket
        /// </summary>
        private DataWriter bluetoothWriter;

        /// <summary>
        /// Datareader created from the socket
        /// </summary>
        private DataReader bluetoothReader;

        #region Display Name

        private string displayNameValue = "";

        /// <summary>
        /// Get the name of the device we are connected to
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (internalStatus == ManagerStatus.GotConnection)
                {
                    return displayNameValue;
                }
                else
                {
                    return "";
                }
            }
        }

        #endregion

        #region Manager Status

        public enum ManagerStatus
        {
            Idle,
            GettingConnection,
            GotConnection,
            FailedToGetConnection,
            LostConnection
        }


        /// <summary>
        /// Bind to events that are fired when the Bluetooth connection changes status.
        /// </summary>
        /// <param name="status">new status value</param>
        public delegate void StatusChangedDelegate(ManagerStatus status);

        public StatusChangedDelegate StatusChangedNotification;

        private ManagerStatus internalStatusValue = ManagerStatus.Idle;

        internal ManagerStatus internalStatus
        {
            get
            {
                return internalStatusValue;
            }
            set
            {
                internalStatusValue = value;

                if (StatusChangedNotification != null)
                    StatusChangedNotification(internalStatusValue);
            }
        }

        /// <summary>
        /// True if the connection is OK
        /// </summary>
        public ManagerStatus Status
        {
            get
            {
                return internalStatus;
            }
        }

        #endregion

        #region Diagnostic Messages

        string diagnosticStringValue = "";

        /// <summary>
        /// Gives the status of the Bluetooth at any time, as a string
        /// </summary>
        public string DiagnosticMessage
        {
            get
            {
                return diagnosticStringValue;
            }
        }

        private string DiagnosticStringNotify
        {
            set
            {
                diagnosticStringValue = value;
                sendStatusChangedNotification(value);
            }
        }

        public delegate void DiagnosticsMessageDelegate(string message);

        /// <summary>
        /// Bind to this to get diagnostic status messages.
        /// </summary>
        public DiagnosticsMessageDelegate DiagnosticsChangedNotification;

        void sendStatusMessage(object message)
        {
            if (DiagnosticsChangedNotification != null)
                DiagnosticsChangedNotification((string)message);
        }

        void sendStatusChangedNotification(string message)
        {
            IAsyncAction ThreadPoolWorkItem = Windows.System.Threading.ThreadPool.RunAsync(
                (source) =>
                {
                    if (DiagnosticsChangedNotification != null)
                        DiagnosticsChangedNotification((string)message);
                }
                , Windows.System.Threading.WorkItemPriority.Normal
                );
        }

        #endregion

        #region Response methods

        public delegate void GotMessageDelegate(byte[] message);

        /// <summary>
        /// Bind to this to get notification of each incoming message.
        /// </summary>
        public GotMessageDelegate GotMessageNotification;

        void sendReadNotification(byte[] message)
        {
            IAsyncAction ThreadPoolWorkItem = Windows.System.Threading.ThreadPool.RunAsync(
                (source) =>
                {
                    if (GotMessageNotification != null)
                        GotMessageNotification((byte[])message);
                }
                , Windows.System.Threading.WorkItemPriority.Normal
                );
        }

        /// <summary>
        /// Bind to this to get notification of successful message transmission.
        /// </summary>
        public delegate void SentMessageDelegate();

        public SentMessageDelegate SentMessageNotification;

        void sendSent()
        {
            if (SentMessageNotification != null)
            {
                SentMessageNotification();
            }
        }

        void sendSentMessageNotification()
        {
            IAsyncAction ThreadPoolWorkItem = Windows.System.Threading.ThreadPool.RunAsync(
                (source) =>
                {
                    sendSent();
                }
                , Windows.System.Threading.WorkItemPriority.Normal
                );
        }

        #endregion

        public BluetoothManager()
        {
            displayNameValue = "";
        }

        /// <summary>
        /// May be used to reset the interface if it has got stuck.
        /// For now it just resets the status so that we can try to use the device
        /// again.
        /// </summary>
        public void ResetToIdle()
        {
            internalStatus = ManagerStatus.Idle;
        }

        /// <summary>
        /// Initialises a Bluetooth connection to a Bluetooth serial device.
        /// There must only be one in range, it will bind to the first one if more than one are available.
        /// Returns immediately, the state will change when 
        /// the connection is available.
        /// </summary>
        public async void Initialise(string bluetoothDeviceName)
        {
            if (internalStatus != ManagerStatus.Idle)
            {
                DiagnosticStringNotify = "Bluetooth interface already active";
                return;
            }

            doSetup(bluetoothDeviceName);
        }

        /// <summary>
        /// Sets up a Bluetooth connection. Runs in a thread by Initialise
        /// </summary>
        /// <param name="bluetoothDeviceName">name of device to search for</param>
        async private void doSetup(string bluetoothDeviceName)
        {
            if (internalStatus == ManagerStatus.Idle)
            {

                deviceCollection = null;

                DiagnosticStringNotify = "Getting connection";

                internalStatus = ManagerStatus.GettingConnection;

                try
                {
                    // Find all paired instances of the Rfcomm chat service
                    deviceCollection = await DeviceInformation.FindAllAsync(
                        RfcommDeviceService.GetDeviceSelector(RfcommServiceId.SerialPort), new string[] { "System.Devices.AepService.AepId" });
                }
                catch (Exception ex)
                {
                    DiagnosticStringNotify = "Finding paired devices failed: " + ex.Message;
                    internalStatus = ManagerStatus.FailedToGetConnection;
                    return;
                }

                if (deviceCollection.Count == 0)
                {
                    DiagnosticStringNotify = "No devices found";
                    internalStatus = ManagerStatus.FailedToGetConnection;
                    return;
                }

                DeviceInformation device = null;

                foreach (DeviceInformation search in deviceCollection)
                {
                    var deviceInfo = await DeviceInformation.CreateFromIdAsync((string)search.Properties["System.Devices.AepService.AepId"]);
                    if (deviceInfo.Name.ToUpper() == bluetoothDeviceName.ToUpper())
                    {
                        device = search;
                        break;
                    }
                }

                if (device == null)
                {
                    DiagnosticStringNotify = "Device not found: " + bluetoothDeviceName;
                    internalStatus = ManagerStatus.FailedToGetConnection;
                    return;
                }

                DiagnosticStringNotify = "Got paired device";

                displayNameValue = device.Name;

                if (bluetoothSocket != null)
                {
                    // Disposing the socket with close it and release all resources associated with the socket
                    bluetoothSocket.Dispose();
                }

                try
                {
                    deviceService = await RfcommDeviceService.FromIdAsync(device.Id);

                    if (deviceService == null)
                    {
                        DiagnosticStringNotify = "Failed to get device service";
                        internalStatus = ManagerStatus.FailedToGetConnection;
                        return;
                    }

                    bluetoothSocket = new StreamSocket();

                    await bluetoothSocket.ConnectAsync(deviceService.ConnectionHostName, deviceService.ConnectionServiceName);

                    bluetoothWriter = new DataWriter(bluetoothSocket.OutputStream);
                    bluetoothWriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                    bluetoothWriter.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                    bluetoothReader = new DataReader(bluetoothSocket.InputStream);
                    bluetoothReader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                    bluetoothReader.ByteOrder = ByteOrder.LittleEndian;

                    internalStatus = ManagerStatus.GotConnection;

                }
                catch (Exception ex)
                {
                    DiagnosticStringNotify = "Exception creating socket" + ex.Message;
                    internalStatus = ManagerStatus.FailedToGetConnection;
                    return;
                }
            }

            // Tell people - will fire the message even if we are already connected

            if (internalStatus == ManagerStatus.GotConnection)
            {
                internalStatus = ManagerStatus.GotConnection;
                DiagnosticStringNotify = "Got connection";
            }
        }

        /// <summary>
        /// Helper method to convert strings into bytes 
        /// </summary>
        /// <param name="text">input string</param>
        /// <returns>UTF8 encoded array of bytes</returns>
        static byte[] TextToBytes(string text)
        {
            System.Text.UTF8Encoding UTF8 = new System.Text.UTF8Encoding();
            Byte[] messageBytes = UTF8.GetBytes((string)text);
            return messageBytes;
        }

        /// <summary>
        /// Helper method to convert bytes into strings. 
        /// Does a quick and dirty UTF8 conversion
        /// </summary>
        /// <param name="text">array of bytes</param>
        /// <returns>string of text</returns>
        static string BytesToText(byte[] text)
        {
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                s.Append((char)text[i]);
            }
            return s.ToString();
        }

        /// <summary>
        /// Sends a line of text to the target device. Returns immediately, the transfer
        /// is performed by a thread.
        /// </summary>
        /// <param name="messageString">Message to be sent.</param>
        public void SendBytes(byte[] bytes)
        {
            IAsyncAction ThreadPoolWorkItem = Windows.System.Threading.ThreadPool.RunAsync(
                (source) =>
                {
                    doSendBytes(bytes);
                }
                , Windows.System.Threading.WorkItemPriority.Normal
                );
        }

        /// <summary>
        /// Sends a string of text to the target device. 
        /// </summary>
        /// <param name="message">message to be sent. Only use the ASCII character range of UNICODE.</param>
        /// <returns>true if the transmission was successful.</returns>
        public async Task<bool> SendStringAsync(string message)
        {
            if (internalStatus != ManagerStatus.GotConnection) return false;

            byte[] buffer = new byte[message.Length];

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)message[i];
            }

            try
            {
                bluetoothWriter.WriteBytes(buffer);
                await bluetoothWriter.StoreAsync();
                await bluetoothWriter.FlushAsync();
            }
            catch
            {
                DiagnosticStringNotify = "Lost connection on write";
                internalStatus = ManagerStatus.LostConnection;
                return false;
            }
            return true;
        }

        async void doSendBytes(object message)
        {
            if (internalStatus != ManagerStatus.GotConnection) return;

            Byte[] messageBytes = (Byte[])message;

            try
            {
                bluetoothWriter.WriteBytes(messageBytes);
                await bluetoothWriter.StoreAsync();
                await bluetoothWriter.FlushAsync();
            }
            catch
            {
                DiagnosticStringNotify = "Lost connection on write";
                internalStatus = ManagerStatus.LostConnection;
            }
            sendSentMessageNotification();
        }

        /// <summary>
        /// Initiates a read request. Returns immediately. Use the 
        /// GotMessageDelegate to get notification of received data.
        /// </summary>
        public void ReadRequest()
        {
            IAsyncAction ThreadPoolWorkItem = Windows.System.Threading.ThreadPool.RunAsync(
                (source) =>
                {
                    doReadRequest();
                }
                , Windows.System.Threading.WorkItemPriority.Normal
                );
        }

        async void doReadRequest()
        {
            if (internalStatus != ManagerStatus.GotConnection) return;

            try
            {

                byte[] destination = new byte[100];
                var readStatus = await bluetoothSocket.InputStream.ReadAsync(
                    destination.AsBuffer(),
                    (uint)destination.Length,
                    InputStreamOptions.Partial);
                if (readStatus.Length > 0)
                {
                    byte[] result = new byte[readStatus.Length];
                    Array.Copy(destination, result, (int)readStatus.Length);
                    sendReadNotification(result);
                }
            }
            catch
            {
                DiagnosticStringNotify = "Lost connection on read";
                internalStatus = BluetoothManager.ManagerStatus.LostConnection;
            }
        }
    }

    public class MessageAssembler
    {
        public static byte[] Build(byte[] source)
        {
            List<byte> message = new List<byte>();

            byte check = 0;

            message.Add(0xff); // drop the start value

            message.Add((byte)(source.Length + 1));

            foreach (byte b in source)
            {
                check += b;

                if (b < 0xFE)
                {
                    message.Add(b);
                }
                else
                {
                    message.Add(0xFE);

                    if (b == 0xFE)
                        message.Add(0x01);
                    else
                        message.Add(0x02);
                }
            }

            if (check < 0xFE)
            {
                message.Add(check);
            }
            else
            {
                message.Add(0xFE);

                if (check == 0xFE)
                    message.Add(0x01);
                else
                    message.Add(0x02);
            }
            return message.ToArray();
        }

        enum RemoteCommandReceiveState
        {
            awaitingStartState,
            awaitingLengthState,
            bufferingCharsState,
            awaitingEscapeChar,
            awaitingChecksumState
        };

        byte[] buffer;
        byte commandPos;
        byte commandSize;

        static RemoteCommandReceiveState remoteCommandState;

        void resetRemoteCommandReceiver()
        {
            remoteCommandState = RemoteCommandReceiveState.awaitingStartState;
        }

        public delegate void DeliverMessageDelegate(byte[] message);

        /// <summary>
        /// Bind to this to get notification of each incoming message.
        /// </summary>
        public DeliverMessageDelegate DeliverMessage;

        void sendBuffer()
        {
            if (DeliverMessage != null)
            {
                byte[] duplicate = new byte[buffer.Length];
                Array.Copy(buffer, duplicate, buffer.Length);
                IAsyncAction ThreadPoolWorkItem = Windows.System.Threading.ThreadPool.RunAsync(
                    (source) =>
                    {
                        DeliverMessage((byte[])duplicate);
                    }
                    , Windows.System.Threading.WorkItemPriority.Normal
                    );
            }
        }

        void bufferByte(byte b)
        {
            buffer[commandPos] = b;
            commandPos++;
            if (commandPos == commandSize)
            {
                byte check = 0;
                for (byte i = 0; i < commandSize - 1; i++)
                {
                    check += buffer[i];
                }

                if (check != buffer[commandSize - 1])
                {
                    return;
                }
                sendBuffer();
                remoteCommandState = RemoteCommandReceiveState.awaitingStartState;
            }
        }

        void processRemoteCommandByte(byte b)
        {
            switch (remoteCommandState)
            {
                case RemoteCommandReceiveState.awaitingStartState:
                    if (b == 0xff)
                        remoteCommandState = RemoteCommandReceiveState.awaitingLengthState;
                    break;
                case RemoteCommandReceiveState.awaitingLengthState:
                    if (b > buffer.Length)
                        remoteCommandState = RemoteCommandReceiveState.awaitingStartState;
                    else
                    {
                        commandPos = 0;
                        commandSize = b;
                        remoteCommandState = RemoteCommandReceiveState.bufferingCharsState;
                    }
                    break;
                case RemoteCommandReceiveState.bufferingCharsState:
                    if (b == 0xFF)
                    {
                        remoteCommandState = RemoteCommandReceiveState.awaitingLengthState;
                        break;
                    }
                    if (b == 0xFE)
                    {
                        // start of an escape sequence
                        remoteCommandState = RemoteCommandReceiveState.awaitingEscapeChar;
                        break;
                    }
                    // Add the character to the buffer
                    // Process the command if at the end
                    bufferByte(b);
                    break;
                case RemoteCommandReceiveState.awaitingEscapeChar:
                    if (b == 1)
                    {
                        bufferByte(0xFE);
                        remoteCommandState = RemoteCommandReceiveState.bufferingCharsState;
                    }
                    else if (b == 2)
                    {
                        bufferByte(0xFF);
                        remoteCommandState = RemoteCommandReceiveState.bufferingCharsState;
                    }
                    else
                        resetRemoteCommandReceiver();
                    break;
            }
        }

        public MessageAssembler(int size)
        {
            buffer = new byte[size];
        }
    }
}

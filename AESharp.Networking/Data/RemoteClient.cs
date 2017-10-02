﻿using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using AESharp.Networking.Exceptions;
using AESharp.Networking.Middleware;

namespace AESharp.Networking.Data
{
    /// <summary>
    ///     Implements most functionality needed for a remote client, but must be inherited to handle packets
    /// </summary>
    public abstract class RemoteClient<TMetaPacket> where TMetaPacket : MetaPacket, new()
    {
        private const int BufferSize = 4096;

        private Guid _clientGuid = Guid.Empty;

        public TcpClient RawClient { get; }

        /// <summary>
        ///     Guid used to identify this client - generated by MasterRouter and can only be allocated once.
        /// </summary>
        public Guid ClientGuid
        {
            get => _clientGuid;
            set
            {
                if (_clientGuid != Guid.Empty)
                    throw new InvalidOperationException($"{nameof(ClientGuid)} can only be allocated once.");

                if (value == Guid.Empty)
                    throw new InvalidOperationException(
                        $"Guid.Empty is not a valid value for property {nameof(ClientGuid)}");

                _clientGuid = value;
            }
        }

        /// <summary>
        ///     True if the underlying TcpClient is connected - if false the RemoteClient is invalid and should no longer be used.
        /// </summary>
        public bool Connected => RawClient.Connected;

        protected RemoteClient(TcpClient rawClient)
        {
            RawClient = rawClient;
        }

        /// <summary>
        ///     Begins listening for data, calling this.HandleDataAsync when data is received
        /// </summary>
        /// <returns>Task</returns>
        public async Task ListenForDataTask()
        {
            if (RawClient == null)
                throw new NullReferenceException($"{nameof(RawClient)} cannot be null");

            if (!Connected)
                throw new InvalidOperationException("Must be connected to listen for data");

            var ns = RawClient.GetStream();

            while (Connected)
            {
                var buffer = new byte[BufferSize];
                var bytesRead = await ns.ReadAsync(buffer, 0, buffer.Length);

                if (bytesRead == 0)
                {
                    Disconnect();
                    break;
                }

                Array.Resize(ref buffer, bytesRead);

                try
                {
                    var metaPacket = new TMetaPacket { Payload = buffer };
                    await HandleDataAsync(metaPacket);

                    if (metaPacket.KillSender)
                    {
                        Disconnect();
                        break;
                    }
                }
                catch (InvalidPacketException ex)
                {
                    Console.WriteLine(ex.Message);
                    Disconnect();
                }
            }
        }

        /// <summary>
        ///     Sends data to the RemoteClient
        /// </summary>
        /// <param name="metaPacket">Packet to send</param>
        /// <returns>Task</returns>
        public virtual async Task SendDataAsync(TMetaPacket metaPacket)
        {
            var data = metaPacket.Payload;

            await RawClient.GetStream().WriteAsync(data, 0, data.Length);
        }

        /// <summary>
        ///     Handles data sent by the client
        /// </summary>
        /// <param name="metaPacket">MetaPacket containing data that was sent by the client</param>
        /// <returns>Task</returns>
        public abstract Task HandleDataAsync(TMetaPacket metaPacket);

        /// <summary>
        ///     Closes both receive and send sockets for the underlying TcpClient. After calling this method, the RemoteClient
        ///     becomes invalid.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                RawClient?.Client?.Shutdown(SocketShutdown.Both);
            }
            // Socket has already been closed
            catch (ObjectDisposedException)
            {
            }
        }

        ~RemoteClient()
        {
            // Disconnect sockets before destroying object
            Disconnect();
        }
    }
}
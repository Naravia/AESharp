﻿using System;
using System.Threading.Tasks;
using AESharp.Routing.Exceptions;
using AESharp.Routing.Networking.Packets.Handshaking;

namespace AESharp.Routing.Networking.Packets
{
    public class AEPacketHandler<TPacketContext> where TPacketContext : AERoutingClient
    {
        private static readonly Func<AEPacket, TPacketContext, Task> NullHandler =
            ( packet, context ) => { throw new UnhandledAEPacketException( (int) packet.PacketId ); };

        public Func<ClientHandshakeBeginPacket, TPacketContext, Task> ClientHandshakeBeginHandler = NullHandler;
        public Func<ServerHandshakeResultPacket, TPacketContext, Task> ServerHandshakeResultHandler = NullHandler;

        public async Task HandlePacket( AEPacket packet, TPacketContext context )
        {
            byte[] data = packet.FinalizePacket();

            switch ( packet.PacketId )
            {
                case AEPacketId.ClientHandshakeBegin:
                    await this.ClientHandshakeBeginHandler( new ClientHandshakeBeginPacket( data ), context );
                    break;
                case AEPacketId.ServerHandshakeResult:
                    await this.ServerHandshakeResultHandler( new ServerHandshakeResultPacket( data ), context );
                    break;
            }
        }
    }
}
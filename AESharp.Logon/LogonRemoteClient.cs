﻿using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using AESharp.Core.Extensions;
using AESharp.Logon.Universal.Networking.Middleware;
using AESharp.Logon.Universal.Networking.Packets;
using AESharp.Networking.Data;
using AESharp.Networking.Data.Packets;
using AESharp.Networking.Exceptions;

namespace AESharp.Logon
{
    public class LogonRemoteClient : RemoteClient<LogonMetaPacket>
    {
        public LogonAuthenticationData AuthData { get; } = new LogonAuthenticationData();

        public LogonRemoteClient(TcpClient rawClient) : base(rawClient)
        {
        }

        public override async Task SendDataAsync(LogonMetaPacket metaPacket)
        {
            metaPacket = await LogonServices.OutgoingLogonMiddleware.RunMiddlewareAsync(metaPacket, this);

            if (metaPacket.Handled)
            {
                Console.WriteLine("Outgoing logon middleware handled metaPacket");
                return;
            }

            await base.SendDataAsync(metaPacket);
        }

        public override async Task HandleDataAsync(LogonMetaPacket metaPacket)
        {
            metaPacket = await LogonServices.IncomingLogonMiddleware.RunMiddlewareAsync(metaPacket, this);
            if (metaPacket.Handled)
            {
                Console.WriteLine("Incoming logon middleware handled metaPacket");
                return;
            }

            var logonPacket = new LogonPacket(metaPacket);

            switch (logonPacket.Opcode)
            {
                case (byte) LogonOpcodes.Challenge:
                {
                    var packet = new ChallengePacket(logonPacket);
                    Console.WriteLine("Received logon packet:");
                    Console.WriteLine($"\tError:\t\t\t{packet.Error}");
                    Console.WriteLine($"\tSize:\t\t\t{packet.Size}");
                    Console.WriteLine($"\tGame:\t\t\t{packet.Game}");
                    Console.WriteLine($"\tBuild:\t\t\t{packet.Build}");
                    Console.WriteLine($"\tPlatform:\t\t{packet.Platform}");
                    Console.WriteLine($"\tOS:\t\t\t{packet.OS}");
                    Console.WriteLine($"\tCountry:\t\t{packet.Country}");
                    Console.WriteLine($"\tTimezone Bias:\t\t{packet.TimezoneBias}");
                    Console.WriteLine($"\tIP:\t\t\t{packet.IP}");
                    Console.WriteLine($"\tAccount Name:\t\t{packet.AccountName}");

                    Console.Write($"Validating username... ");
                    var account = LogonServices.Accounts.GetAccount(packet.AccountName);
                    if (account == null)
                    {
                        Console.WriteLine($"failed. Account {packet.AccountName} does not exist.");

                        var response = new ChallengeResponsePacket
                        {
                            Error = ChallengeResponsePacket.ChallengeResponseError.NoSuchAccount
                        };
                        await SendDataAsync(new LogonMetaPacket(response.FinalizePacket()));
                    }
                    else
                    {
                        Console.WriteLine("success!");

                        if (account.Banned)
                        {
                            Console.WriteLine($"Account {account.Username} is currently banned.");
                            var response = new ChallengeResponsePacket
                            {
                                Error = ChallengeResponsePacket.ChallengeResponseError.AccountClosed
                            };
                            await SendDataAsync(new LogonMetaPacket(response.FinalizePacket()));
                            Disconnect();
                            return;
                        }

                        AuthData.DbAccount = account;

                        Console.WriteLine($"Validating username and password for account {account.Username}");

                        AuthData.InitSRP6(account.Username,
                            account.PasswordHash.ByteRepresentationToByteArray());

                        var pack = new Packet();
                        pack.WriteByte(0);
                        pack.WriteByte(0);
                        pack.WriteByte(0);
                        var b = AuthData.Srp6.PublicEphemeralValueB;
                        pack.WriteBytes(b.GetBytes(32));

                        pack.WriteByte(1);
                        pack.WriteBytes(AuthData.Srp6.Generator.GetBytes(1));

                        pack.WriteByte(32);
                        pack.WriteBytes(AuthData.Srp6.Modulus.GetBytes(32));

                        pack.WriteBytes(AuthData.Srp6.Salt.GetBytes(32));

                        var rand = new Random(Environment.TickCount);
                        var randBytes = new byte[16];
                        rand.NextBytes(randBytes);
                        pack.WriteBytes(randBytes);

                        pack.WriteByte(0);

                        await SendDataAsync(new LogonMetaPacket(pack.FinalizePacket()));
                    }
                    break;
                }
                case (byte) LogonOpcodes.Proof:
                {
                    var proofPacket = new ProofPacket(logonPacket);

                    var proofValid = AuthData.Srp6.IsClientProofValid(proofPacket.A, proofPacket.M1);

                    Console.WriteLine($"Authentication {(proofValid ? "successful" : "failed")}");

                    if (!proofValid)
                    {
                        var response = new ChallengeResponsePacket
                        {
                            Error = ChallengeResponsePacket.ChallengeResponseError.NoSuchAccount
                        };
                        await SendDataAsync(new LogonMetaPacket(response.FinalizePacket()));
                        return;
                    }

                    var successPacket = new Packet();
                    successPacket.WriteByte(0x1);
                    successPacket.WriteByte(0x0);
                    successPacket.WriteBytes(AuthData.Srp6.ServerSessionKeyProof.GetBytes(20));
                    successPacket.WriteInt32(0);
                    successPacket.WriteInt32(0);
                    successPacket.WriteInt16(0);

                    await SendDataAsync(new LogonMetaPacket(successPacket.FinalizePacket()));

                    break;
                }
                case (byte) LogonOpcodes.RealmList:
                {
                    var realms = LogonServices.Realms.GetRealms();

                    var realmCount = (short) realms.Count;

                    Console.WriteLine($"Sending {realmCount} realms");

                    var realmPacket = new Packet();
                    realmPacket.WriteByte(0x10);

                    var oldPosition = realmPacket.BufferPosition;
                    realmPacket.WriteInt16(0);
                    realmPacket.WriteInt32(0);

                    realmPacket.WriteInt16(realmCount);

                    foreach (var realm in realms)
                    {
                        realmPacket.WriteByte((byte) realm.Type);
                        realmPacket.WriteBoolean(realm.IsLocked);
                        realmPacket.WriteByte((byte) realm.Flags);
                        realmPacket.WriteCString(realm.Name);
                        realmPacket.WriteCString(realm.Address);
                        realmPacket.WriteSingle(realm.Population);
                        realmPacket.WriteByte(3); // Characters
                        realmPacket.WriteByte((byte) realm.Region);
                        realmPacket.WriteByte(0); // Unk
                    }

                    realmPacket.WriteByte(0x10);
                    realmPacket.WriteByte(0x0);

                    realmPacket.BufferPosition = oldPosition;
                    realmPacket.WriteInt16((short) (realmPacket.Length - 3));

                    await SendDataAsync(new LogonMetaPacket(realmPacket.FinalizePacket()));

                    break;
                }
                default:
                {
                    throw new InvalidPacketException($"Received unsupported opcode: 0x{logonPacket.Opcode:x2}");
                }
            }
        }
    }
}
﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
#if false
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.MixedReality.Sharing.Matchmaking.Local
{
    /// <summary>
    /// Joined room belonging to a different host.
    /// </summary>
    class ForeignRoom : RoomBase
    {
        public readonly SocketerClient Socket;

        private event EventHandler<int> AttributesChangeReceived;
        public override event EventHandler<MessageReceivedArgs> MessageReceived;

        // TODO see SetAttributesAsync
        private static int Combine(int hash1, int hash2)
        {
            int hash = 17;
            hash = hash * 31 + hash1;
            hash = hash * 31 + hash2;
            return hash;
        }
        // TODO see SetAttributesAsync
        private static int GetHash(IReadOnlyDictionary<string, object> attributes)
        {
            int hash = 0;
            foreach (var attr in attributes)
            {
                hash = Combine(hash, attr.Key.GetHashCode());
                hash = Combine(hash, attr.Value.GetHashCode());
            }
            return hash;
        }

        public static async Task<ForeignRoom> Connect(RoomInfo roomInfo, MatchParticipant localParticipant, CancellationToken token)
        {
            SocketerClient socket = SocketerClient.CreateSender(SocketerClient.Protocol.TCP, roomInfo.Host, roomInfo.Port);

            // Make a room.
            var res = new ForeignRoom(roomInfo, null /* TODO */, socket);

            // Configure handlers.
            socket.Message += res.OnMessage;
            socket.Disconnected += (SocketerClient server, int id, string clientHost, int clientPort) =>
            {
                // TODO notify service
                socket.Stop();
            };

            // Connect.
            {
                var ev = new TaskCompletionSource<object>();
                Action<SocketerClient, int, string, int> connectHandler =
                (SocketerClient server, int id, string clientHost, int clientPort) =>
                {
                    // Wake up the original task.
                    ev.TrySetResult(null);
                };
                socket.Connected += connectHandler;
                socket.Start();
                using (var reg = token.Register(() => ev.SetCanceled()))
                {
                    await ev.Task;
                }
                socket.Connected -= connectHandler;
            }


            // Start the handshake and wait until the room attributes have been sent.
            // This means that we have received a consistent room state.
            {
                var ev = new TaskCompletionSource<object>();

                EventHandler handler = (object o, EventArgs e) =>
                {
                    // Wake up the original task.
                    ev.TrySetResult(null);
                };
                res.AttributesChanged += handler;
                // Send participant info to the server.
                socket.SendNetworkMessage(Utils.CreateJoinRequestPacket(localParticipant));
                using (var reg = token.Register(() => ev.SetCanceled()))
                {
                    await ev.Task;
                }
                res.AttributesChanged -= handler;
            }

            // Now that the handshake is done, we can return the room.
            return res;
        }

        // Assumes that `socket` is configured and will be connected by the caller.
        public ForeignRoom(RoomInfo roomInfo, MatchParticipant owner, SocketerClient socket)
            : base(roomInfo, owner)
        {
            // Clear the participants array, will be filled by the server.
            // TODO cleanup
            Participants = new RoomParticipant[0];
            Socket = socket;
        }

        private void OnMessage(SocketerClient server, SocketerClient.MessageEvent ev)
        {
            switch (Utils.ParseHeader(ev.Message))
            {
                case Utils.AttrHeader:
                {
                    // Apply the changes locally.
                    var recvAttributes = Utils.ParseAttrPacket(ev.Message);
                    IReadOnlyDictionary<string, object> oldAttributes = Attributes;
                    Dictionary<string, object> newAttributes = new Dictionary<string, object>(oldAttributes.Count);
                    foreach (var attr in oldAttributes)
                    {
                        newAttributes.Add(attr.Key, attr.Value);
                    }
                    foreach (var attr in recvAttributes)
                    {
                        newAttributes[attr.Key] = attr.Value;
                    }
                    Attributes = newAttributes;

                    // TODO see SetAttributesAsync
                    AttributesChangeReceived?.Invoke(this, GetHash(recvAttributes));

                    // Raise the event.
                    RaiseAttributesChanged();
                    break;
                }
                case Utils.MsgHeader:
                {
                    // Raise the event.
                    int senderId = Utils.ParseMessageParticipant(ev.Message);
                    var sender = Participants.First(p => p.IdInRoom == senderId);
                    MessageReceived?.Invoke(this, new MessageReceivedArgs(sender, Utils.ParseMessagePayload(ev.Message)));
                    break;
                }
                case Utils.PartJoinedHeader:
                {
                    AddParticipant(Utils.ParseParticipantJoinedPacket(ev.Message));
                    break;
                }
                case Utils.PartLeftHeader:
                {
                    RemoveParticipant(Utils.ParseParticipantLeftPacket(ev.Message));
                    break;
                }
            }
        }

        public override Task LeaveAsync()
        {
            return Task.Run(() => Socket.Stop());
        }

        public override Task SetAttributesAsync(Dictionary<string, object> attributes)
        {
            // TODO match set-attribute request and response by the attributes hash.
            // This is naive and should be replaced by state sync
            int attrCode = GetHash(attributes);
            var ev = new ManualResetEventSlim();
            EventHandler<int> handler = (object o, int code) =>
            {
                if(code == attrCode)
                {
                    ev.Set();
                }
            };
            AttributesChangeReceived += handler;

            // Send the set-attribute request to the server.
            // TODO shouldn't send inside the lock but only enqueue packets
            lock (this)
            {
                Socket.SendNetworkMessage(Utils.CreateAttrPacket(attributes));
            }

            // Wait for the corresponding response.
            return Task.Run(() =>
            {
                ev.Wait();
                AttributesChangeReceived -= handler;
            });
        }

        public override Task SetVisibility(RoomVisibility val)
        {
            throw new NotImplementedException();
        }

        public override void SendMessage(RoomParticipant participant, byte[] message)
        {
            var packet = Utils.CreateMessagePacket(participant.IdInRoom, message);
            Socket.SendNetworkMessage(packet);
        }

        public override void Dispose()
        {
            LeaveAsync().Wait();
        }
    }
}
#endif
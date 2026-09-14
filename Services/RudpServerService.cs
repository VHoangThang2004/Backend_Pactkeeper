using LiteNetLib;
using LiteNetLib.Utils;
using GameInventoryApi.RealTime;
using System.Numerics;

namespace GameInventoryApi.Services;

public class RudpServerService
{
    private readonly EventBasedNetListener _listener;
    private readonly NetManager _manager;
    private readonly NetDataWriter _writer = new();

    public RudpServerService()
    {
        _listener = new EventBasedNetListener();
        _manager = new NetManager(_listener);
        _manager.UpdateTime = 15;

        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.ConnectionRequestEvent += request => request.AcceptIfKey("");
    }

    public void Start()
    {
        _manager.Start(7778);
        Console.WriteLine("[RUDP] Real-time Relay Server started on port 7778");
    }

    private void OnPeerConnected(NetPeer peer)
    {
        Console.WriteLine($"[RUDP] Client connected: {peer.Address}");
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Console.WriteLine($"[RUDP] Client disconnected: {peer.Address}");
    }
    private void OnNetworkReceive(NetPeer fromPeer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        var state = new PlayerState
        {
            PlayerId = reader.GetString(),
            Username = reader.GetString(),
            Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat()),
            RotationY = reader.GetFloat()
        };

        Console.WriteLine($"[RUDP] Received from {state.Username} at {state.Position}");

        _writer.Reset();
        _writer.Put(state.PlayerId);
        _writer.Put(state.Username);
        _writer.Put(state.Position.X);
        _writer.Put(state.Position.Y);
        _writer.Put(state.Position.Z);
        _writer.Put(state.RotationY);

        foreach (var peer in _manager.ConnectedPeerList)
        {
            if (peer != fromPeer)
                peer.Send(_writer, DeliveryMethod.Unreliable);
        }
    }
    public void PollEvents()
    {
        _manager.PollEvents();
    }

    public void Stop()
    {
        _manager.Stop();
    }
}
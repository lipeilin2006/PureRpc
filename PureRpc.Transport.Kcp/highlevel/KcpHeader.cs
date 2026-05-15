using System;

namespace PureRpc.Transport.Kcp
{
    // header for messages processed by kcp.
    // this is NOT for the raw receive messages(!) because handshake/disconnect
    // need to be sent reliably. it's not enough to have those in rawreceive
    // because those messages might get lost without being resent!
    public enum KcpHeaderReliable : byte
    {
        // don't react on 0x00. might help to filter out random noise.
        Hello      = 1,
        // ping goes over reliable & KcpHeader for now. could go over unreliable
        // too. there is no real difference except that this is easier because
        // we already have a KcpHeader for reliable messages.
        // ping is only used to keep it alive, so latency doesn't matter.
        Ping       = 2,
        Pong       = 4, // '4' not '3' in order to keep backwards compatibility
        Data       = 3,
    }

    public enum KcpHeaderUnreliable : byte
    {
        // users may send unreliable messages
        Data = 4,
        // disconnect always goes through rapid fire unreliable (glenn fielder)
        Disconnect = 5,
    }

    // save convert the enums from/to byte.
    // attackers may attempt to send invalid values, so '255' may not convert.
    public static class KcpHeader
    {
        public static bool ParseReliable(byte value, out KcpHeaderReliable header)
        {
            switch (value)
            {
                case 1: header = KcpHeaderReliable.Hello;  return true;
                case 2: header = KcpHeaderReliable.Ping;   return true;
                case 3: header = KcpHeaderReliable.Data;   return true;
                case 4: header = KcpHeaderReliable.Pong;   return true;
                default:
                    header = KcpHeaderReliable.Ping;
                    return false;
            }
        }

        public static bool ParseUnreliable(byte value, out KcpHeaderUnreliable header)
        {
            switch (value)
            {
                case 4: header = KcpHeaderUnreliable.Data;       return true;
                case 5: header = KcpHeaderUnreliable.Disconnect; return true;
                default:
                    header = KcpHeaderUnreliable.Disconnect;
                    return false;
            }
        }
    }
}

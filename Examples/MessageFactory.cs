﻿using PacketNet.Message;


namespace Examples
{
    public static class MessageFactory
    {
        public static T Create<T>() where T : AbstractNetMessage, new()
        {
            return MFactory<T>.GetMessage();
        }


        public static ExampleMessage ExampleMessage(Int64 value)
        {
            var msg = MFactory<ExampleMessage>.GetMessage();
            msg.X = value;
            return msg;
        }

    }
}
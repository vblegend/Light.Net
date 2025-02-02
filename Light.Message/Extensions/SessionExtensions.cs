﻿using Light.Message;
using Light.Transmit;
using System.Threading.Tasks;



namespace System
{
    public static class SessionExtensions
    {
        public static void Write(this IConnectionSession context, AbstractNetMessage message)
        {
            using (var writer = new MessageWriter(context.Writer))
            {
                writer.Write(message);
            }

        }



        public static async ValueTask WriteFlushAsync(this IConnectionSession context, AbstractNetMessage message)
        {
            using (var writer = new MessageWriter(context.Writer))
            {
                writer.Write(message);
            }
            await context.FlushAsync();
        }
    }
}

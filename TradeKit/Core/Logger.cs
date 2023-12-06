using System;

namespace TradeKit.Core
{
    public static class Logger
    {
        private static Action<string> m_writeAction;

        /// <summary>
        /// Sets the log write delegate.
        /// </summary>
        /// <param name="writeAction">The write action.</param>
        public static void SetWrite(Action<string> writeAction)
        {
            m_writeAction = writeAction;
        }

        /// <summary>
        /// Writes the specified message to the log.
        /// </summary>
        /// <param name="message">The message.</param>
        public static void Write(string message)
        {
            m_writeAction?.Invoke(message);
        }
    }
}

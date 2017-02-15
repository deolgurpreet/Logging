// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Logging.Console.Internal
{
    public class ConsoleLoggerProcessor : IDisposable
    {
        private const int _maxQueuedMessages = 1024;

        private readonly BlockingCollection<LogMessageEntry> _messageQueue = new BlockingCollection<LogMessageEntry>(_maxQueuedMessages);
        private readonly Task _outputTask;

        public ConsoleLoggerProcessor()
        {
            // Start Console message queue processor
            _outputTask = Task.Factory.StartNew(
                ProcessLogQueue,
                this,
                TaskCreationOptions.LongRunning);
        }

        public void EnqueueMessage(LogMessageEntry message)
        {
            _messageQueue.Add(message);
        }

        private void ProcessLogQueue()
        {
            foreach (var message in _messageQueue.GetConsumingEnumerable())
            {
                if (message.LevelString != null)
                {
                    message.Console.Write(message.LevelString, message.LevelBackground, message.LevelForeground);
                }

                message.Console.Write(message.Message, message.MessageColor, message.MessageColor);
                message.Console.Flush();
            }
        }

        private static void ProcessLogQueue(object state)
        {
            var consoleLogger = (ConsoleLoggerProcessor)state;

            consoleLogger.ProcessLogQueue();
        }

        public void Dispose()
        {
            _messageQueue.CompleteAdding();

            try
            {
                _outputTask.Wait(1500); // with timeout in-case Console is locked by user input
            }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && ex.InnerExceptions[0] is TaskCanceledException) { }
        }
    }
}

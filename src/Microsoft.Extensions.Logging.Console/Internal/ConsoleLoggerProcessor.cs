// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Logging.Console.Internal
{
    public class ConsoleLoggerProcessor
    {
        private const int _maxQueuedMessages = 1024;
        // Writing to console is not an atomic operation in the current implementation and since multiple logger 
        // instances are created with a different name. Also since Console is global, using a static lock is fine. 
        private static readonly object _lock = new object();

        private IConsole _console;

        private readonly BlockingCollection<LogMessageEntry> _messageQueue = new BlockingCollection<LogMessageEntry>(_maxQueuedMessages);
        private readonly Task _outputTask;

        public ConsoleLoggerProcessor()
        {
            RegisterForExit();

            // Start Console message queue processor
            _outputTask = Task.Factory.StartNew(
                ProcessLogQueue,
                this,
                TaskCreationOptions.LongRunning);
        }

        public IConsole Console
        {
            get { return _console; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                _console = value;
            }
        }

        public bool HasQueuedMessages => _messageQueue.Count > 0;

        public void EnqueueMessage(LogMessageEntry message)
        {
            _messageQueue.Add(message);
        }

        private void ProcessLogQueue()
        {
            foreach (var message in _messageQueue.GetConsumingEnumerable())
            {
                lock (_lock)
                {
                    if (message.LevelString != null)
                    {
                        Console.Write(message.LevelString, message.LevelBackground, message.LevelForeground);
                    }

                    Console.Write(message.Message, message.MessageColor, message.MessageColor);
                    Console.Flush();
                }
            }
        }

        private static void ProcessLogQueue(object state)
        {
            var consoleLogger = (ConsoleLoggerProcessor)state;

            consoleLogger.ProcessLogQueue();
        }

        private void RegisterForExit()
        {
            // Hooks to detect Process exit, and allow the Console to complete output
#if NET451
            AppDomain.CurrentDomain.ProcessExit += InitiateShutdown;
#elif NETSTANDARD1_5
            var currentAssembly = typeof(ConsoleLogger).GetTypeInfo().Assembly;
            System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(currentAssembly).Unloading += InitiateShutdown;
#endif
        }

#if NET451
        private void InitiateShutdown(object sender, EventArgs e)
#elif NETSTANDARD1_5
        private void InitiateShutdown(System.Runtime.Loader.AssemblyLoadContext obj)
#else
        private void InitiateShutdown()
#endif
        {
            // TODO: Do after _outputTask.Wait(...) in case there are items blocked on getting added?
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

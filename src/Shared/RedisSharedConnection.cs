//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Microsoft.Web.Redis
{
    internal class RedisSharedConnection
    {
        private ProviderConfiguration _configuration;
        private ConfigurationOptions _configOption;
        private static readonly ConcurrentQueue<ConnectionMultiplexer> _connectionPool = new ConcurrentQueue<ConnectionMultiplexer>();

        // Used for mocking in testing
        internal RedisSharedConnection()
        { }

        public RedisSharedConnection(ProviderConfiguration configuration)
        {
            _configuration = configuration;

            // If connection string is given then use it otherwise use individual options
            if (!string.IsNullOrEmpty(configuration.ConnectionString))
            {
                _configOption = ConfigurationOptions.Parse(configuration.ConnectionString);
                // Setting explicitly 'abortconnect' to false. It will overwrite customer provided value for 'abortconnect'
                // As it doesn't make sense to allow to customer to set it to true as we don't give them access to ConnectionMultiplexer
                // in case of failure customer can not create ConnectionMultiplexer so right choice is to automatically create it by providing AbortOnConnectFail = false
                _configOption.AbortOnConnectFail = false;
            }
            else
            {
                _configOption = new ConfigurationOptions();
                if (configuration.Port == 0)
                {
                    _configOption.EndPoints.Add(configuration.Host);
                }
                else
                {
                    _configOption.EndPoints.Add(configuration.Host + ":" + configuration.Port);
                }
                _configOption.Password = configuration.AccessKey;
                _configOption.Ssl = configuration.UseSsl;
                _configOption.AbortOnConnectFail = false;

                if (configuration.ConnectionTimeoutInMilliSec != 0)
                {
                    _configOption.ConnectTimeout = configuration.ConnectionTimeoutInMilliSec;
                }

                if (configuration.OperationTimeoutInMilliSec != 0)
                {
                    _configOption.SyncTimeout = configuration.OperationTimeoutInMilliSec;
                }
            }
            CreateMultiplexer();
        }

        public ConnectionMultiplexer DequeueConnection()
        {
            if (!_connectionPool.TryDequeue(out var connection))
            {
                connection = CreateMultiplexer();
            }
            return connection;
        }

        public void EnqueueConnection(ConnectionMultiplexer connectionMultiplexer)
        {
            if (connectionMultiplexer != null)
                _connectionPool.Enqueue(connectionMultiplexer);
        }

        public IDatabase GetDatabase(ConnectionMultiplexer connectionMultiplexer)
        {
            return connectionMultiplexer.GetDatabase(_configOption.DefaultDatabase ?? _configuration.DatabaseId);
        }

        public void ForceReconnect(ref ConnectionMultiplexer connection)
        {
            //no need to lock old connection, because every ConnectionMultiplexer belongs to a single thread, and we dequeues this connection from pool to not use by another thread.
            CloseMultiplexer(connection);
            connection = CreateMultiplexer();
        }

        private ConnectionMultiplexer CreateMultiplexer()
        {
            if (LogUtility.logger == null)
            {
                return ConnectionMultiplexer.Connect(_configOption);
            }
            else
            {
                return ConnectionMultiplexer.Connect(_configOption, LogUtility.logger);
            }
        }

        private void CloseMultiplexer(ConnectionMultiplexer oldMultiplexer)
        {
            if (oldMultiplexer != null)
            {
                try
                {
                    oldMultiplexer.Close();
                    oldMultiplexer.Dispose();
                    oldMultiplexer = null;
                }
                catch (Exception)
                {
                    // Example error condition: if accessing old.Value causes a connection attempt and that fails. 
                }
            }
        }

    }
}

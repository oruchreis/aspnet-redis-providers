//
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
//

using System;
using System.Threading.Tasks;

namespace Microsoft.Web.Redis
{
    internal class RedisOutputCacheConnectionWrapper : IOutputCacheConnection
    {
        internal static RedisSharedConnection sharedConnection;
        static object lockForSharedConnection = new object();
        internal static RedisUtility redisUtility;

        internal IRedisClientConnection redisConnection;
        ProviderConfiguration configuration;
        
        public RedisOutputCacheConnectionWrapper(ProviderConfiguration configuration)
        {
            this.configuration = configuration;

            // only single object of RedisSharedConnection will be created and then reused
            if (sharedConnection == null)
            {
                lock (lockForSharedConnection)
                {
                    if (sharedConnection == null)
                    {
                        sharedConnection = new RedisSharedConnection(configuration);
                        redisUtility = new RedisUtility(configuration);
                    }
                }
            }
            redisConnection = new StackExchangeClientConnection(configuration, redisUtility, sharedConnection);
        }

/*-------Start of Add operation-----------------------------------------------------------------------------------------------------------------------------------------------*/
        // KEYS = { key }
        // ARGV = { page data, expiry time in miliseconds } 
        // retArray = { page data from cache or new }
        static readonly string addScript = (@"
                    local retVal = redis.call('GET',KEYS[1])
                    if retVal == false then
                       redis.call('PSETEX',KEYS[1],ARGV[2],ARGV[1])
                       retVal = ARGV[1]
                    end
                    return retVal
                    ");

        public async Task<object> AddAsync(string key, object entry, DateTime utcExpiry)
        {
            key = GetKeyForRedis(key);
            TimeSpan expiryTime = utcExpiry - DateTime.UtcNow;
            string[] keyArgs = new string[] { key };
            object[] valueArgs = new object[] { redisUtility.GetBytesFromObject(entry), (long) expiryTime.TotalMilliseconds };

            object rowDataFromRedis = await redisConnection.EvalAsync(addScript, keyArgs, valueArgs).ConfigureAwait(false);
            return redisUtility.GetObjectFromBytes(redisConnection.GetOutputCacheDataFromResult(rowDataFromRedis));
        }

/*-------End of Add operation-----------------------------------------------------------------------------------------------------------------------------------------------*/

        public async Task SetAsync(string key, object entry, DateTime utcExpiry)
        {
            key = GetKeyForRedis(key);
            byte[] data = redisUtility.GetBytesFromObject(entry);
            await redisConnection.SetAsync(key, data, utcExpiry).ConfigureAwait(false);
        }

        public async Task<object> GetAsync(string key)
        {
            key = GetKeyForRedis(key);
            byte[] data = await redisConnection.GetAsync(key).ConfigureAwait(false);
            return redisUtility.GetObjectFromBytes(data);
        }

        public async Task RemoveAsync(string key)
        {
            key = GetKeyForRedis(key);
            await redisConnection.RemoveAsync(key).ConfigureAwait(false);
        }

        private string GetKeyForRedis(string key)
        {
            return configuration.ApplicationName + "_" + key;
        }

        public void Set(string key, object entry, DateTime utcExpiry)
        {
            key = GetKeyForRedis(key);
            byte[] data = redisUtility.GetBytesFromObject(entry);
            redisConnection.Set(key, data, utcExpiry);
        }

        public object Add(string key, object entry, DateTime utcExpiry)
        {
            key = GetKeyForRedis(key);
            TimeSpan expiryTime = utcExpiry - DateTime.UtcNow;
            string[] keyArgs = new string[] { key };
            object[] valueArgs = new object[] { redisUtility.GetBytesFromObject(entry), (long)expiryTime.TotalMilliseconds };

            object rowDataFromRedis = redisConnection.Eval(addScript, keyArgs, valueArgs);
            return redisUtility.GetObjectFromBytes(redisConnection.GetOutputCacheDataFromResult(rowDataFromRedis));
        }

        public object Get(string key)
        {
            key = GetKeyForRedis(key);
            byte[] data = redisConnection.Get(key);
            return redisUtility.GetObjectFromBytes(data);
        }

        public void Remove(string key)
        {
            key = GetKeyForRedis(key);
            redisConnection.Remove(key);
        }
    }
}

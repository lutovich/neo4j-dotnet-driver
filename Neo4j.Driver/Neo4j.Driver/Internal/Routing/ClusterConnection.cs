// Copyright (c) 2002-2017 "Neo Technology,"
// Network Engine for Objects in Lund AB [http://neotechnology.com]
// 
// This file is part of Neo4j.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Neo4j.Driver.Internal.Connector;
using Neo4j.Driver.V1;

namespace Neo4j.Driver.Internal.Routing
{
    internal class ClusterConnection : DelegatedConnection
    {
        private readonly Uri _uri;
        private readonly AccessMode _mode;
        private readonly IClusterErrorHandler _errorHandler;

        public ClusterConnection(IConnection connection, Uri uri, AccessMode mode, IClusterErrorHandler errorHandler)
        :base(connection)
        {
            _uri = uri;
            _mode = mode;
            _errorHandler = errorHandler;
        }

        public override void OnError(Exception error)
        {
            if (error is ServiceUnavailableException)
            {
                _errorHandler.OnConnectionError(_uri, error);
                throw new SessionExpiredException(
                    $"Server at {_uri} is no longer available due to error: {error.Message}.", error);
            }
            else if (error.IsClusterError())
            {
                switch (_mode)
                {
                    case AccessMode.Read:
                        // The user was trying to run a write in a read session
                        // So inform the user and let him try with a proper session mode
                        throw new ClientException("Write queries cannot be performed in READ access mode.");
                    case AccessMode.Write:
                        // The lead is no longer a leader, a.k.a. the write server no longer accepts writes
                        // However the server is still available for possible reads.
                        // Therefore we just remove it from ClusterView but keep it in connection pool.
                        _errorHandler.OnWriteError(_uri);
                        throw new SessionExpiredException($"Server at {_uri} no longer accepts writes");
                    default:
                        throw new ArgumentOutOfRangeException($"Unsupported mode type {_mode}");
                }
            }
            throw error;
        }
    }
}
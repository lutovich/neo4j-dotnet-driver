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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Neo4j.Driver.Internal.Connector;
using Neo4j.Driver.Internal.Messaging;
using Neo4j.Driver.V1;

namespace Neo4j.Driver.Internal.Packstream
{
    internal class PackStreamMessageFormatV1
    {
        public PackStreamMessageFormatV1(ITcpSocketClient tcpSocketClient, ILogger logger, bool supportBytes=true)
        {
            var output = new ChunkedOutputStream(tcpSocketClient, logger);
            var input = new ChunkedInputStream(tcpSocketClient, logger);
            if (supportBytes)
            {
                Writer = new WriterV1(output);
                Reader = new ReaderV1(input);
            }
            else
            {
                Writer = new WriterBytesIncompatibleV1(output);
                Reader = new ReaderBytesIncompatibleV1(input);
            }
        }

        public class WriterBytesIncompatibleV1 : WriterV1
        {
            public WriterBytesIncompatibleV1(IChunkedOutputStream outputStream) : base(outputStream)
            {
            }
            public override void PackValue(object value)
            {
                if (value is byte[])
                {
                    throw new ProtocolException($"Cannot understand { nameof(value) } with type { value.GetType().FullName}");
                }
                base.PackValue(value);
            }
        }

        public class ReaderBytesIncompatibleV1 : ReaderV1
        {
            public ReaderBytesIncompatibleV1(IChunkedInputStream inputStream) : base(inputStream)
            {
            }

            public override object UnpackValue(PackStream.PackType type)
            {
                if (type == PackStream.PackType.Bytes)
                {
                    throw new ProtocolException($"Unsupported type {type}.");
                }
                return base.UnpackValue(type);
            }
        }

        public IWriter Writer { get; }
        public IReader Reader { get; }

        public class ReaderV1 : IReader
        {
            private static readonly Dictionary<string, object> EmptyStringValueMap = new Dictionary<string, object>();
            private readonly IChunkedInputStream _inputStream;
            private readonly PackStream.Unpacker _unpacker;

            public ReaderV1(IChunkedInputStream inputStream)
            {
                _inputStream = inputStream;
                _unpacker = new PackStream.Unpacker(_inputStream);
            }

            public void Read(IMessageResponseHandler responseHandler)
            {
                _unpacker.UnpackStructHeader();
                var type = _unpacker.UnpackStructSignature();

                switch (type)
                {
                    case MSG_RECORD:
                        UnpackRecordMessage(responseHandler);
                        break;
                    case MSG_SUCCESS:
                        UnpackSuccessMessage(responseHandler);
                        break;
                    case MSG_FAILURE:
                        UnpackFailureMessage(responseHandler);
                        break;
                    case MSG_IGNORED:
                        UnpackIgnoredMessage(responseHandler);
                        break;
                    default:
                        throw new ProtocolException("Unknown requestMessage type: " + type);
                }
                UnPackMessageTail();
            }

            public async Task ReadAsync(IMessageResponseHandler responseHandler)
            {
                await _unpacker.UnpackStructHeaderAsync().ConfigureAwait(false);
                var type = await _unpacker.UnpackStructSignatureAsync().ConfigureAwait(false);

                switch (type)
                {
                    case MSG_RECORD:
                        await UnpackRecordMessageAsync(responseHandler).ConfigureAwait(false);
                        break;
                    case MSG_SUCCESS:
                        await UnpackSuccessMessageAsync(responseHandler).ConfigureAwait(false);
                        break;
                    case MSG_FAILURE:
                        await UnpackFailureMessageAsync(responseHandler).ConfigureAwait(false);
                        break;
                    case MSG_IGNORED:
                        UnpackIgnoredMessage(responseHandler);
                        break;
                    default:
                        throw new ProtocolException("Unknown requestMessage type: " + type);
                }
                await UnPackMessageTailAsync().ConfigureAwait(false);
            }

            public object UnpackValue()
            {
                var type = _unpacker.PeekNextType();
                return UnpackValue(type);
            }

            public async Task<object> UnpackValueAsync()
            {
                var type = await _unpacker.PeekNextTypeAsync().ConfigureAwait(false);
                return await UnpackValueAsync(type).ConfigureAwait(false);
            }

            public virtual object UnpackValue(PackStream.PackType type)
            {
                switch (type)
                {
                    case PackStream.PackType.Bytes:
                        return _unpacker.UnpackBytes();
                    case PackStream.PackType.Null:
                        return _unpacker.UnpackNull();
                    case PackStream.PackType.Boolean:
                        return _unpacker.UnpackBoolean();
                    case PackStream.PackType.Integer:
                        return _unpacker.UnpackLong();
                    case PackStream.PackType.Float:
                        return _unpacker.UnpackDouble();
                    case PackStream.PackType.String:
                        return _unpacker.UnpackString();
                    case PackStream.PackType.Map:
                        return UnpackMap();
                    case PackStream.PackType.List:
                        return UnpackList();
                    case PackStream.PackType.Struct:
                        long size = _unpacker.UnpackStructHeader();
                        switch (_unpacker.UnpackStructSignature())
                        {
                            case NODE:
                                Throw.ProtocolException.IfNotEqual(NodeFields, size, nameof(NodeFields), nameof(size));
                                return UnpackNode();
                            case RELATIONSHIP:
                                Throw.ProtocolException.IfNotEqual(RelationshipFields, size, nameof(RelationshipFields),
                                    nameof(size));
                                return UnpackRelationship();
                            case PATH:
                                Throw.ProtocolException.IfNotEqual(PathFields, size, nameof(PathFields), nameof(size));
                                return UnpackPath();
                        }
                        break;
                }
                throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown value type: {type}");
            }

            public virtual async Task<object> UnpackValueAsync(PackStream.PackType type)
            {
                switch (type)
                {
                    case PackStream.PackType.Bytes:
                        return await _unpacker.UnpackBytesAsync().ConfigureAwait(false);
                    case PackStream.PackType.Null:
                        return await _unpacker.UnpackNullAsync().ConfigureAwait(false);
                    case PackStream.PackType.Boolean:
                        return await _unpacker.UnpackBooleanAsync().ConfigureAwait(false);
                    case PackStream.PackType.Integer:
                        return await _unpacker.UnpackLongAsync().ConfigureAwait(false);
                    case PackStream.PackType.Float:
                        return await _unpacker.UnpackDoubleAsync().ConfigureAwait(false);
                    case PackStream.PackType.String:
                        return await _unpacker.UnpackStringAsync().ConfigureAwait(false);
                    case PackStream.PackType.Map:
                        return await UnpackMapAsync().ConfigureAwait(false);
                    case PackStream.PackType.List:
                        return await UnpackListAsync().ConfigureAwait(false);
                    case PackStream.PackType.Struct:
                        long size = await _unpacker.UnpackStructHeaderAsync().ConfigureAwait(false);
                        switch (await _unpacker.UnpackStructSignatureAsync().ConfigureAwait(false))
                        {
                            case NODE:
                                Throw.ProtocolException.IfNotEqual(NodeFields, size, nameof(NodeFields), nameof(size));
                                return await UnpackNodeAsync().ConfigureAwait(false); ;
                            case RELATIONSHIP:
                                Throw.ProtocolException.IfNotEqual(RelationshipFields, size, nameof(RelationshipFields),
                                    nameof(size));
                                return await UnpackRelationshipAsync().ConfigureAwait(false);
                    case PATH:
                                Throw.ProtocolException.IfNotEqual(PathFields, size, nameof(PathFields), nameof(size));
                                return await UnpackPathAsync().ConfigureAwait(false);
                }
                        break;
                }
                throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown value type: {type}");
            }

            private IPath UnpackPath()
            {
                // List of unique nodes
                var uniqNodes = new INode[(int) _unpacker.UnpackListHeader()];
                for (int i = 0; i < uniqNodes.Length; i++)
                {
                    Throw.ProtocolException.IfNotEqual(NodeFields, _unpacker.UnpackStructHeader(), nameof(NodeFields),
                        $"received{nameof(NodeFields)}");
                    Throw.ProtocolException.IfNotEqual(NODE, _unpacker.UnpackStructSignature(), nameof(NODE),
                        $"received{nameof(NODE)}");
                    uniqNodes[i] = UnpackNode();
                }

                // List of unique relationships, without start/end information
                var uniqRels = new Relationship[(int) _unpacker.UnpackListHeader()];
                for (int i = 0; i < uniqRels.Length; i++)
                {
                    Throw.ProtocolException.IfNotEqual(UnboundRelationshipFields, _unpacker.UnpackStructHeader(),
                        nameof(UnboundRelationshipFields), $"received{nameof(UnboundRelationshipFields)}");
                    Throw.ProtocolException.IfNotEqual(UNBOUND_RELATIONSHIP, _unpacker.UnpackStructSignature(),
                        nameof(UNBOUND_RELATIONSHIP), $"received{nameof(UNBOUND_RELATIONSHIP)}");
                    var urn = _unpacker.UnpackLong();
                    var relType = _unpacker.UnpackString();
                    var props = UnpackMap();
                    uniqRels[i] = new Relationship(urn, -1, -1, relType, props);
                }

                // Path sequence
                var length = (int) _unpacker.UnpackListHeader();

                // Knowing the sequence length, we can create the arrays that will represent the nodes, rels and segments in their "path order"
                var segments = new ISegment[length / 2];
                var nodes = new INode[segments.Length + 1];
                var rels = new IRelationship[segments.Length];

                var prevNode = uniqNodes[0];
                INode nextNode; // Start node is always 0, and isn't encoded in the sequence
                Relationship rel;
                nodes[0] = prevNode;
                for (int i = 0; i < segments.Length; i++)
                {
                    int relIdx = (int) _unpacker.UnpackLong();
                    nextNode = uniqNodes[(int) _unpacker.UnpackLong()];
                    // Negative rel index means this rel was traversed "inversed" from its direction
                    if (relIdx < 0)
                    {
                        rel = uniqRels[(-relIdx) - 1]; // -1 because rel idx are 1-indexed
                        rel.SetStartAndEnd(nextNode.Id, prevNode.Id);
                    }
                    else
                    {
                        rel = uniqRels[relIdx - 1];
                        rel.SetStartAndEnd(prevNode.Id, nextNode.Id);
                    }

                    nodes[i + 1] = nextNode;
                    rels[i] = rel;
                    segments[i] = new Segment(prevNode, rel, nextNode);
                    prevNode = nextNode;
                }
                return new Path(segments.ToList(), nodes.ToList(), rels.ToList());
            }

            private async Task<IPath> UnpackPathAsync()
            {
                // List of unique nodes
                var uniqNodes = new INode[(int)await _unpacker.UnpackListHeaderAsync().ConfigureAwait(false)];
                for (int i = 0; i < uniqNodes.Length; i++)
                {
                    Throw.ProtocolException.IfNotEqual(NodeFields, await _unpacker.UnpackStructHeaderAsync().ConfigureAwait(false), nameof(NodeFields),
                        $"received{nameof(NodeFields)}");
                    Throw.ProtocolException.IfNotEqual(NODE, await _unpacker.UnpackStructSignatureAsync().ConfigureAwait(false), nameof(NODE),
                        $"received{nameof(NODE)}");
                    uniqNodes[i] = await UnpackNodeAsync().ConfigureAwait(false);
                }

                // List of unique relationships, without start/end information
                var uniqRels = new Relationship[(int)await _unpacker.UnpackListHeaderAsync().ConfigureAwait(false)];
                for (int i = 0; i < uniqRels.Length; i++)
                {
                    Throw.ProtocolException.IfNotEqual(UnboundRelationshipFields, await _unpacker.UnpackStructHeaderAsync().ConfigureAwait(false),
                        nameof(UnboundRelationshipFields), $"received{nameof(UnboundRelationshipFields)}");
                    Throw.ProtocolException.IfNotEqual(UNBOUND_RELATIONSHIP, await _unpacker.UnpackStructSignatureAsync().ConfigureAwait(false),
                        nameof(UNBOUND_RELATIONSHIP), $"received{nameof(UNBOUND_RELATIONSHIP)}");
                    var urn = await _unpacker.UnpackLongAsync().ConfigureAwait(false);
                    var relType = await _unpacker.UnpackStringAsync().ConfigureAwait(false);
                    var props = await UnpackMapAsync().ConfigureAwait(false);
                    uniqRels[i] = new Relationship(urn, -1, -1, relType, props);
                }

                // Path sequence
                var length = (int)await _unpacker.UnpackListHeaderAsync().ConfigureAwait(false);

                // Knowing the sequence length, we can create the arrays that will represent the nodes, rels and segments in their "path order"
                var segments = new ISegment[length / 2];
                var nodes = new INode[segments.Length + 1];
                var rels = new IRelationship[segments.Length];

                var prevNode = uniqNodes[0];
                INode nextNode; // Start node is always 0, and isn't encoded in the sequence
                Relationship rel;
                nodes[0] = prevNode;
                for (int i = 0; i < segments.Length; i++)
                {
                    int relIdx = (int)await _unpacker.UnpackLongAsync().ConfigureAwait(false);
                    nextNode = uniqNodes[(int)await _unpacker.UnpackLongAsync().ConfigureAwait(false)];
                    // Negative rel index means this rel was traversed "inversed" from its direction
                    if (relIdx < 0)
                    {
                        rel = uniqRels[(-relIdx) - 1]; // -1 because rel idx are 1-indexed
                        rel.SetStartAndEnd(nextNode.Id, prevNode.Id);
                    }
                    else
                    {
                        rel = uniqRels[relIdx - 1];
                        rel.SetStartAndEnd(prevNode.Id, nextNode.Id);
                    }

                    nodes[i + 1] = nextNode;
                    rels[i] = rel;
                    segments[i] = new Segment(prevNode, rel, nextNode);
                    prevNode = nextNode;
                }
                return new Path(segments.ToList(), nodes.ToList(), rels.ToList());
            }

            private IRelationship UnpackRelationship()
            {
                var urn = _unpacker.UnpackLong();
                var startUrn = _unpacker.UnpackLong();
                var endUrn = _unpacker.UnpackLong();
                var relType = _unpacker.UnpackString();
                var props = UnpackMap();

                return new Relationship(urn, startUrn, endUrn, relType, props);
            }

            private async Task<IRelationship> UnpackRelationshipAsync()
            {
                var urn = await _unpacker.UnpackLongAsync().ConfigureAwait(false);
                var startUrn = await _unpacker.UnpackLongAsync().ConfigureAwait(false);
                var endUrn = await _unpacker.UnpackLongAsync().ConfigureAwait(false);
                var relType = await _unpacker.UnpackStringAsync().ConfigureAwait(false);
                var props = await UnpackMapAsync().ConfigureAwait(false);

                return new Relationship(urn, startUrn, endUrn, relType, props);
            }

            private INode UnpackNode()
            {
                var urn = _unpacker.UnpackLong();

                var numLabels = (int) _unpacker.UnpackListHeader();
                var labels = new List<string>(numLabels);
                for (var i = 0; i < numLabels; i++)
                {
                    labels.Add(_unpacker.UnpackString());
                }
                var numProps = (int) _unpacker.UnpackMapHeader();
                var props = new Dictionary<string, object>(numProps);
                for (var j = 0; j < numProps; j++)
                {
                    var key = _unpacker.UnpackString();
                    props.Add(key, UnpackValue());
                }

                return new Node(urn, labels, props);
            }

            private async Task<INode> UnpackNodeAsync()
            {
                var urn = await _unpacker.UnpackLongAsync().ConfigureAwait(false);

                var numLabels = (int) await _unpacker.UnpackListHeaderAsync().ConfigureAwait(false);
                var labels = new List<string>(numLabels);
                for (var i = 0; i < numLabels; i++)
                {
                    labels.Add(await _unpacker.UnpackStringAsync().ConfigureAwait(false));
                }
                var numProps = (int)await _unpacker.UnpackMapHeaderAsync().ConfigureAwait(false);
                var props = new Dictionary<string, object>(numProps);
                for (var j = 0; j < numProps; j++)
                {
                    var key = await _unpacker.UnpackStringAsync().ConfigureAwait(false);
                    props.Add(key, await UnpackValueAsync().ConfigureAwait(false));
                }

                return new Node(urn, labels, props);
            }

            private void UnpackIgnoredMessage(IMessageResponseHandler responseHandler)
            {
                responseHandler.HandleIgnoredMessage();
            }

            private void UnpackFailureMessage(IMessageResponseHandler responseHandler)
            {
                var values = UnpackMap();
                var code = values["code"]?.ToString();
                var message = values["message"]?.ToString();
                responseHandler.HandleFailureMessage(code, message);
            }

            private async Task UnpackFailureMessageAsync(IMessageResponseHandler responseHandler)
            {
                var values = await UnpackMapAsync().ConfigureAwait(false);
                var code = values["code"]?.ToString();
                var message = values["message"]?.ToString();
                responseHandler.HandleFailureMessage(code, message);
            }

            private void UnpackRecordMessage(IMessageResponseHandler responseHandler)
            {
                var fieldCount = (int) _unpacker.UnpackListHeader();
                var fields = new object[fieldCount];
                for (var i = 0; i < fieldCount; i++)
                {
                    fields[i] = UnpackValue();
                }
                responseHandler.HandleRecordMessage(fields);
            }

            private async Task UnpackRecordMessageAsync(IMessageResponseHandler responseHandler)
            {
                var fieldCount = (int) await _unpacker.UnpackListHeaderAsync().ConfigureAwait(false);
                var fields = new object[fieldCount];
                for (var i = 0; i < fieldCount; i++)
                {
                    fields[i] = await UnpackValueAsync().ConfigureAwait(false);
                }
                responseHandler.HandleRecordMessage(fields);
            }


            private void UnPackMessageTail()
            {
                _inputStream.ReadMessageTail();
            }

            private Task UnPackMessageTailAsync()
            {
                return _inputStream.ReadMessageTailAsync();
            }

            private void UnpackSuccessMessage(IMessageResponseHandler responseHandler)
            {
                var map = UnpackMap();
                responseHandler.HandleSuccessMessage(map);
            }

            private async Task UnpackSuccessMessageAsync(IMessageResponseHandler responseHandler)
            {
                var map = await UnpackMapAsync().ConfigureAwait(false);
                responseHandler.HandleSuccessMessage(map);
            }

            private Dictionary<string, object> UnpackMap()
            {
                var size = (int) _unpacker.UnpackMapHeader();
                if (size == 0)
                {
                    return EmptyStringValueMap;
                }
                var map = new Dictionary<string, object>(size);
                for (var i = 0; i < size; i++)
                {
                    var key = _unpacker.UnpackString();
                    map.Add(key, UnpackValue());
                }
                return map;
            }

            private async Task<Dictionary<string, object>> UnpackMapAsync()
            {
                var size = (int) await _unpacker.UnpackMapHeaderAsync().ConfigureAwait(false);
                if (size == 0)
                {
                    return EmptyStringValueMap;
                }
                var map = new Dictionary<string, object>(size);
                for (var i = 0; i < size; i++)
                {
                    var key = await _unpacker.UnpackStringAsync().ConfigureAwait(false);
                    map.Add(key, await UnpackValueAsync().ConfigureAwait(false));
                }
                return map;
            }

            private IList<object> UnpackList()
            {
                var size = (int) _unpacker.UnpackListHeader();
                var vals = new object[size];
                for (var j = 0; j < size; j++)
                {
                    vals[j] = UnpackValue();
                }
                return new List<object>(vals);
            }

            private async Task<IList<object>> UnpackListAsync()
            {
                var size = (int) await _unpacker.UnpackListHeaderAsync().ConfigureAwait(false);
                var vals = new object[size];
                for (var j = 0; j < size; j++)
                {
                    vals[j] = await UnpackValueAsync().ConfigureAwait(false);
                }
                return new List<object>(vals);
            }

        }

        public class WriterV1 : IWriter, IMessageRequestHandler
        {
            private readonly IChunkedOutputStream _outputStream;
            private readonly PackStream.Packer _packer;


            public WriterV1(IChunkedOutputStream outputStream)
            {
                _outputStream = outputStream;
                _packer = new PackStream.Packer(_outputStream);
            }

            public void HandleInitMessage(string clientNameAndVersion, IDictionary<string, object> authToken)
            {
                _packer.PackStructHeader(1, MSG_INIT);
                _packer.Pack(clientNameAndVersion);
                PackRawMap(authToken);
                PackMessageTail();
            }

            public async Task HandleInitMessageAsync(string clientNameAndVersion, IDictionary<string, object> authToken)
            {
                await _packer.PackStructHeaderAsync(1, MSG_INIT).ConfigureAwait(false);
                await _packer.PackAsync(clientNameAndVersion).ConfigureAwait(false);
                await PackRawMapAsync(authToken).ConfigureAwait(false);
                await PackMessageTailAsync().ConfigureAwait(false);
            }

            public void HandleRunMessage(string statement, IDictionary<string, object> parameters)
            {
                _packer.PackStructHeader(2, MSG_RUN);
                _packer.Pack(statement);
                PackRawMap(parameters);
                PackMessageTail();
            }

            public async Task HandleRunMessageAsync(string statement, IDictionary<string, object> parameters)
            {
                await _packer.PackStructHeaderAsync(2, MSG_RUN).ConfigureAwait(false);
                await _packer.PackAsync(statement).ConfigureAwait(false);
                await PackRawMapAsync(parameters).ConfigureAwait(false);
                await PackMessageTailAsync().ConfigureAwait(false);
            }

            public void HandlePullAllMessage()
            {
                _packer.PackStructHeader(0, MSG_PULL_ALL);
                PackMessageTail();
            }

            public async Task HandlePullAllMessageAsync()
            {
                await _packer.PackStructHeaderAsync(0, MSG_PULL_ALL).ConfigureAwait(false);
                await PackMessageTailAsync().ConfigureAwait(false);
            }

            public void HandleDiscardAllMessage()
            {
                _packer.PackStructHeader(0, MSG_DISCARD_ALL);
                PackMessageTail();
            }

            public async Task HandleDiscardAllMessageAsync()
            {
                await _packer.PackStructHeaderAsync(0, MSG_DISCARD_ALL).ConfigureAwait(false);
                await PackMessageTailAsync().ConfigureAwait(false);
            }

            public void HandleResetMessage()
            {
                _packer.PackStructHeader(0, MSG_RESET);
                PackMessageTail();
            }

            public async Task HandleResetMessageAsync()
            {
                await _packer.PackStructHeaderAsync(0, MSG_RESET).ConfigureAwait(false);
                await PackMessageTailAsync().ConfigureAwait(false);
            }

            public void HandleAckFailureMessage()
            {
                _packer.PackStructHeader(0, MSG_ACK_FAILURE);
                PackMessageTail();
            }

            public async Task HandleAckFailureMessageAsync()
            {
                await _packer.PackStructHeaderAsync(0, MSG_ACK_FAILURE).ConfigureAwait(false);
                await PackMessageTailAsync().ConfigureAwait(false);
            }

            public void Write(IRequestMessage requestMessage)
            {
                requestMessage.Dispatch(this);
            }

            public Task WriteAsync(IRequestMessage requestMessage)
            {
                return requestMessage.DispatchAsync(this);
            }

            public void Flush()
            {
                _outputStream.Flush();
            }

            public Task FlushAsync()
            {
                return _outputStream.FlushAsync();
            }

            private void PackMessageTail()
            {
                _outputStream.WriteMessageTail();
            }

            private Task PackMessageTailAsync()
            {
                return _outputStream.WriteMessageTailAsync();
            }

            private void PackRawMap(IDictionary<string, object> dictionary)
            {
                if (dictionary == null || dictionary.Count == 0)
                {
                    _packer.PackMapHeader(0);
                    return;
                }

                _packer.PackMapHeader(dictionary.Count);
                foreach (var item in dictionary)
                {
                    _packer.Pack(item.Key);
                    PackValue(item.Value);
                }
            }

            private async Task PackRawMapAsync(IDictionary<string, object> dictionary)
            {
                if (dictionary == null || dictionary.Count == 0)
                {
                    await _packer.PackMapHeaderAsync(0).ConfigureAwait(false);
                    return;
                }

                await _packer.PackMapHeaderAsync(dictionary.Count).ConfigureAwait(false);
                foreach (var item in dictionary)
                {
                    await _packer.PackAsync(item.Key).ConfigureAwait(false);
                    await PackValueAsync(item.Value).ConfigureAwait(false);
                }
            }

            public virtual void PackValue(object value)
            {
                _packer.Pack(value);
                // the driver should never pack node, relationship or path
            }

            public virtual Task PackValueAsync(object value)
            {
                return _packer.PackAsync(value);
                // the driver should never pack node, relationship or path
            }

        }

        #region Consts

        public const byte MSG_INIT = 0x01;
        public const byte MSG_ACK_FAILURE = 0x0E;
        public const byte MSG_RESET = 0x0F;
        public const byte MSG_RUN = 0x10;
        public const byte MSG_DISCARD_ALL = 0x2F;
        public const byte MSG_PULL_ALL = 0x3F;

        public const byte MSG_RECORD = 0x71;
        public const byte MSG_SUCCESS = 0x70;
        public const byte MSG_IGNORED = 0x7E;
        public const byte MSG_FAILURE = 0x7F;

        public const byte NODE = (byte) 'N';
        public const byte RELATIONSHIP = (byte) 'R';
        public const byte UNBOUND_RELATIONSHIP = (byte) 'r';
        public const byte PATH = (byte) 'P';

        public const long NodeFields = 3;
        public const long RelationshipFields = 5;
        public const long UnboundRelationshipFields = 3;
        public const long PathFields = 3;

        #endregion Consts
    }
}
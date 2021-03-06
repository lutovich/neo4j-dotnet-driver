﻿// Copyright (c) 2002-2017 "Neo Technology,"
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
using System.Linq;
using FluentAssertions;
using Moq;
using Neo4j.Driver.Internal;
using Neo4j.Driver.Internal.Connector;
using Neo4j.Driver.Internal.Routing;
using Neo4j.Driver.V1;
using Xunit;

namespace Neo4j.Driver.Tests.Routing
{
    public class RoutingTableManagerTests
    {

        internal static RoutingTableManager NewRoutingTableManager(
            IRoutingTable routingTable,
            IClusterConnectionPoolManager poolManager,
            Uri seedUri)
        {
            return new RoutingTableManager(
                routingTable,
                new RoutingSettings(new Dictionary<string, string>()),
                poolManager, seedUri, null);
        }

        internal static Mock<IRoutingTable> NewMockedRoutingTable(AccessMode mode, Uri uri, bool hasNext = true)
        {
            var mock = new Mock<IRoutingTable>();
            mock.Setup(m => m.IsStale(It.IsAny<AccessMode>())).Returns(false);
            mock.SetupSequence(m => m.TryNext(mode, out uri)).Returns(hasNext).Returns(false);
            return mock;
        }

        private static IRoutingTable NewRoutingTable(
            IEnumerable<Uri> routers = null,
            IEnumerable<Uri> readers = null,
            IEnumerable<Uri> writers = null)
        {
            // assign default value of uri
            if (routers == null)
            {
                routers = new Uri[0];
            }
            if (readers == null)
            {
                readers = new Uri[0];
            }
            if (writers == null)
            {
                writers = new Uri[0];
            }
            return new RoundRobinRoutingTable(routers, readers, writers, 1000);
        }

        public class UpdateRoutingTableWithInitialUriFallbackMethod
        {
            [Fact]
            public void ShouldPrependInitialRouterIfWriterIsAbsent()
            {
                // Given
                var uri = new Uri("bolt+routing://123:456");

                var routingTableMock = new Mock<IRoutingTable>();
                routingTableMock.Setup(x => x.PrependRouters(It.IsAny<IEnumerable<Uri>>()))
                    .Callback<IEnumerable<Uri>>(r => r.Single().Should().Be(uri));

                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.AddConnectionPool(It.IsAny<IEnumerable<Uri>>()))
                    .Callback<IEnumerable<Uri>>(r => r.Single().Should().Be(uri));

                var manager = NewRoutingTableManager(routingTableMock.Object, poolManagerMock.Object, null);
                manager.IsReadingInAbsenceOfWriter = true;
                var routingTableReturnMock = new Mock<IRoutingTable>();

                // When
                // should throw an exception as the initial routers should not be tried again
                var exception = Record.Exception(() =>
                    manager.UpdateRoutingTableWithInitialUriFallback(new HashSet<Uri> {uri}, c => c != null
                        ? null
                        : routingTableReturnMock.Object));
                exception.Should().BeOfType<ServiceUnavailableException>();

                // Then
                poolManagerMock.Verify(x => x.AddConnectionPool(It.IsAny<IEnumerable<Uri>>()), Times.Once);
                routingTableMock.Verify(x => x.PrependRouters(It.IsAny<IEnumerable<Uri>>()), Times.Once);
            }

            [Fact]
            public void ShouldAddInitialUriWhenNoAvailableRouters()
            {
                // Given
                var uri = new Uri("bolt+routing://123:456");

                var routingTableMock = new Mock<IRoutingTable>();
                routingTableMock.Setup(x => x.PrependRouters(It.IsAny<IEnumerable<Uri>>()))
                    .Callback<IEnumerable<Uri>>(r => r.Single().Should().Be(uri));

                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.AddConnectionPool(It.IsAny<IEnumerable<Uri>>()))
                    .Callback<IEnumerable<Uri>>(r => r.Single().Should().Be(uri));

                var manager = NewRoutingTableManager(routingTableMock.Object, poolManagerMock.Object, null);
                var routingTableReturnMock = new Mock<IRoutingTable>();

                // When
                manager.UpdateRoutingTableWithInitialUriFallback(new HashSet<Uri> {uri}, c => c != null
                    ? null
                    : routingTableReturnMock.Object);

                // Then
                poolManagerMock.Verify(x => x.AddConnectionPool(It.IsAny<IEnumerable<Uri>>()), Times.Once);
                routingTableMock.Verify(x => x.PrependRouters(It.IsAny<IEnumerable<Uri>>()), Times.Once);
            }

            [Fact]
            public void ShouldNotTryInitialUriIfAlreadyTried()
            {
                // Given
                var a = new Uri("bolt+routing://123:456");
                var b = new Uri("bolt+routing://123:789");
                var s = a; // should not be retried
                var t = new Uri("bolt+routing://222:123"); // this should be retried

                var routingTableMock = new Mock<IRoutingTable>();
                routingTableMock.Setup(x => x.PrependRouters(It.IsAny<IEnumerable<Uri>>()))
                    .Callback<IEnumerable<Uri>>(r => r.Single().Should().Be(t));

                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.AddConnectionPool(It.IsAny<IEnumerable<Uri>>()))
                    .Callback<IEnumerable<Uri>>(r => r.Single().Should().Be(t));

                var manager = NewRoutingTableManager(routingTableMock.Object, poolManagerMock.Object, null);

                IRoutingTable UpdateRoutingTableFunc(ISet<Uri> set)
                {
                    if (set != null)
                    {
                        set.Add(a);
                        set.Add(b);
                        return null;
                    }
                    else
                    {
                        return new Mock<IRoutingTable>().Object;
                    }
                }

                // When
                var initialUriSet = new HashSet<Uri> {s, t};
                manager.UpdateRoutingTableWithInitialUriFallback(initialUriSet, UpdateRoutingTableFunc);

                // Then
                // verify the method is actually called
                poolManagerMock.Verify(x => x.AddConnectionPool(It.IsAny<IEnumerable<Uri>>()), Times.Once);
                routingTableMock.Verify(x => x.PrependRouters(It.IsAny<IEnumerable<Uri>>()), Times.Once);
            }
        }

        public class UpdateRoutingTableMethod
        {
            [Fact]
            public void ShouldForgetAndTryNextRouterWhenConnectionIsNull()
            {
                // Given
                var uriA = new Uri("bolt+routing://123:456");
                var uriB = new Uri("bolt+routing://123:789");

                // This ensures that uri and uri2 will return in order
                var routingTable = new ListBasedRoutingTable(new List<Uri> {uriA, uriB});
                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.CreateClusterConnection(It.IsAny<Uri>()))
                    .Returns((ClusterConnection) null);
                var manager = NewRoutingTableManager(routingTable, poolManagerMock.Object, null);

                // When
                var newRoutingTable = manager.UpdateRoutingTable(connection =>
                    throw new NotSupportedException($"Unknown uri: {connection.Server.Address}"));

                // Then
                newRoutingTable.Should().BeNull();
                routingTable.All().Should().BeEmpty();
            }

            [Fact]
            public void ShouldForgetAndTryNextRouterWhenFailedWithConnectionError()
            {
                // Given
                var uriA = new Uri("bolt+routing://123:456");
                var uriB = new Uri("bolt+routing://123:789");
                var connA = new Mock<IConnection>().Object;
                var connB = new Mock<IConnection>().Object;

                // This ensures that uri and uri2 will return in order
                var routingTable = new ListBasedRoutingTable(new List<Uri> {uriA, uriB});
                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.SetupSequence(x => x.CreateClusterConnection(It.IsAny<Uri>()))
                    .Returns(connA).Returns(connB);
                var manager = NewRoutingTableManager(routingTable, poolManagerMock.Object, null);

                // When
                var newRoutingTable = manager.UpdateRoutingTable(connection =>
                {
                    // the second connectin will give a new routingTable
                    if (connection.Equals(connA)) // uriA
                    {
                        routingTable.Remove(uriA);
                        throw new SessionExpiredException("failed init");
                    }
                    if (connection.Equals(connB)) // uriB
                    {
                        return NewRoutingTable(new[] {uriA}, new[] {uriA}, new[] {uriA});
                    }

                    throw new NotSupportedException($"Unknown uri: {connection.Server.Address}");
                });

                // Then
                newRoutingTable.All().Should().ContainInOrder(uriA);
                routingTable.All().Should().ContainInOrder(uriB);
            }

            [Fact]
            public void ShouldPropagateServiceUnavailable()
            {
                var uri = new Uri("bolt+routing://123:456");
                var routingTable = new ListBasedRoutingTable(new List<Uri> { uri });
                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.CreateClusterConnection(uri))
                    .Returns(new Mock<IConnection>().Object);

                var manager = NewRoutingTableManager(routingTable, poolManagerMock.Object, null);

                var exception = Record.Exception(() => manager.UpdateRoutingTable(
                    conn => throw new ServiceUnavailableException("Procedure not found")));

                exception.Should().BeOfType<ServiceUnavailableException>();
                exception.Message.Should().Be("Procedure not found");
            }


            [Fact]
            public void ShouldPropagateProtocolError()
            {
                var uri = new Uri("bolt+routing://123:456");
                var routingTable = new ListBasedRoutingTable(new List<Uri> {uri});
                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.CreateClusterConnection(uri))
                    .Returns(new Mock<IConnection>().Object);
                var manager = NewRoutingTableManager(routingTable, poolManagerMock.Object, null);

                var exception = Record.Exception(() => manager.UpdateRoutingTable(
                    conn => throw new ProtocolException("Cannot parse procedure result")));

                exception.Should().BeOfType<ProtocolException>();
                exception.Message.Should().Be("Cannot parse procedure result");
            }

            [Fact]
            public void ShouldPropagateAuthenticationException()
            {
                // Given
                var uri = new Uri("bolt+routing://123:456");
                var routingTable = new ListBasedRoutingTable(new List<Uri> {uri});
                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.CreateClusterConnection(uri))
                    .Callback(() => throw new AuthenticationException("Failed to auth the client to the server."));
                var manager = NewRoutingTableManager(routingTable, poolManagerMock.Object, null);

                // When
                var error = Record.Exception(() => manager.UpdateRoutingTable());

                // Then
                error.Should().BeOfType<AuthenticationException>();
                error.Message.Should().Contain("Failed to auth the client to the server.");

                // while the server is not removed
                routingTable.All().Should().ContainInOrder(uri);
            }

            [Fact]
            public void ShouldTryNextRouterIfNoReader()
            {
                // Given
                var uriA = new Uri("bolt+routing://123:1");
                var uriB = new Uri("bolt+routing://123:2");
                var connA = new Mock<IConnection>().Object;
                var connB = new Mock<IConnection>().Object;

                var uriX = new Uri("bolt+routing://456:1");
                var uriY = new Uri("bolt+routing://789:2");

                var routingTable = new ListBasedRoutingTable(new List<Uri> {uriA, uriB});
                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.SetupSequence(x => x.CreateClusterConnection(It.IsAny<Uri>()))
                    .Returns(connA).Returns(connB);
                var manager = NewRoutingTableManager(routingTable, poolManagerMock.Object, null);


                // When
                var updateRoutingTable = manager.UpdateRoutingTable(conn =>
                {
                    if (conn.Equals(connA))
                    {
                        return NewRoutingTable(new[] {uriX}, new Uri[0], new[] {uriX});
                    }
                    if (conn.Equals(connB))
                    {
                        return NewRoutingTable(new[] {uriY}, new[] {uriY}, new[] {uriY});
                    }
                    throw new NotSupportedException($"Unknown uri: {conn.Server.Address}");
                });

                // Then
                updateRoutingTable.All().Should().ContainInOrder(uriY);
                manager.IsReadingInAbsenceOfWriter.Should().BeFalse();
            }

            [Fact]
            public void ShouldAcceptRoutingTableIfNoWriter()
            {
                // Given
                var uriA = new Uri("bolt+routing://123:1");
                var connA = new Mock<IConnection>().Object;
                var uriX = new Uri("bolt+routing://456:1");

                var routingTable = new ListBasedRoutingTable(new List<Uri> {uriA});
                var poolManagerMock = new Mock<IClusterConnectionPoolManager>();
                poolManagerMock.Setup(x => x.CreateClusterConnection(It.IsAny<Uri>()))
                    .Returns(connA);
                var manager = NewRoutingTableManager(routingTable, poolManagerMock.Object, null);

                // When
                var updateRoutingTable = manager.UpdateRoutingTable(conn =>
                {
                    if (conn.Equals(connA))
                    {
                        return NewRoutingTable(new[] {uriX}, new[] {uriX});
                    }
                    throw new NotSupportedException($"Unknown uri: {conn.Server.Address}");
                });

                // Then
                updateRoutingTable.All().Should().ContainInOrder(uriX);
                manager.IsReadingInAbsenceOfWriter.Should().BeTrue();
            }
        }

        internal class ListBasedRoutingTable : IRoutingTable
        {
            private readonly List<Uri> _routers;
            private readonly List<Uri> _removed;
            private int _count = 0;

            public ListBasedRoutingTable(List<Uri> routers)
            {
                _routers = routers;
                _removed = new List<Uri>();
            }

            public bool IsStale(AccessMode mode)
            {
                return false;
            }

            public bool TryNextRouter(out Uri uri)
            {
                if (_count >= _routers.Count)
                {
                    uri = null;
                    return false;
                }
                else
                {
                    uri = _routers[_count++];
                    return true;
                }
            }

            public bool TryNextReader(out Uri uri)
            {
                throw new NotSupportedException();
            }

            public bool TryNextWriter(out Uri uri)
            {
                throw new NotSupportedException();
            }

            public bool TryNext(AccessMode mode, out Uri uri)
            {
                throw new NotSupportedException();
            }

            public void Remove(Uri uri)
            {
                _removed.Add(uri);
            }

            public void RemoveWriter(Uri uri)
            {
                throw new NotSupportedException();
            }

            public ISet<Uri> All()
            {
                return new HashSet<Uri>(_routers.Distinct().Except(_removed.Distinct()));
            }

            public void Clear()
            {
                throw new NotSupportedException();
            }

            public void PrependRouters(IEnumerable<Uri> uris)
            {
                throw new NotSupportedException();
            }
        }
    }
}


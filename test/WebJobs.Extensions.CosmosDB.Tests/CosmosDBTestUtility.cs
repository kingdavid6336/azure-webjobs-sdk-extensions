﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Extensions.Tests.Extensions.CosmosDB.Models;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Extensions.CosmosDB.Tests
{
    internal static class CosmosDBTestUtility
    {
        public const string DatabaseName = "ItemDB";
        public const string CollectionName = "ItemCollection";

        // Runs the standard options pipeline for initialization
        public static IOptions<CosmosDBOptions> InitializeOptions(string defaultConnectionString, string optionsConnectionString)
        {
            // Create the Options like we do during host build
            var builder = new HostBuilder()               
                .ConfigureWebJobs(b =>
                {
                    // This wires up our options pipeline.
                    b.AddCosmosDB();

                    // If someone is updating the ConnectionString, they'd do it like this.
                    if (optionsConnectionString != null)
                    {
                        b.Services.Configure<CosmosDBOptions>(o =>
                        {
                            o.ConnectionString = optionsConnectionString;
                        });
                    }
                })
                .ConfigureAppConfiguration(b =>
                {
                    b.Sources.Clear();
                    if (defaultConnectionString != null)
                    {
                        b.AddInMemoryCollection(new Dictionary<string, string>
                        {
                            { $"ConnectionStrings:{Constants.DefaultConnectionStringName}", defaultConnectionString }
                        });
                    }
                });

            return builder.Build().Services.GetService<IOptions<CosmosDBOptions>>();
        }

        public static void SetupCollectionMock(Mock<ICosmosDBService> mockService, string partitionKeyPath = null, int throughput = 0)
        {
            Uri databaseUri = UriFactory.CreateDatabaseUri(DatabaseName);

            var expectedPaths = new List<string>();
            if (!string.IsNullOrEmpty(partitionKeyPath))
            {
                expectedPaths.Add(partitionKeyPath);
            }

            if (throughput == 0)
            {
                mockService
                    .Setup(m => m.CreateDocumentCollectionIfNotExistsAsync(databaseUri,
                        It.Is<DocumentCollection>(d => d.Id == CollectionName && Enumerable.SequenceEqual(d.PartitionKey.Paths, expectedPaths)),
                        null))
                    .ReturnsAsync(new DocumentCollection());
            }
            else
            {
                mockService
                    .Setup(m => m.CreateDocumentCollectionIfNotExistsAsync(databaseUri,
                        It.Is<DocumentCollection>(d => d.Id == CollectionName && Enumerable.SequenceEqual(d.PartitionKey.Paths, expectedPaths)),
                        It.Is<RequestOptions>(r => r.OfferThroughput == throughput)))
                    .ReturnsAsync(new DocumentCollection());
            }
        }

        public static void SetupDatabaseMock(Mock<ICosmosDBService> mockService)
        {
            mockService
                .Setup(m => m.CreateDatabaseIfNotExistsAsync(It.Is<Database>(d => d.Id == DatabaseName)))
                .ReturnsAsync(new Database());
        }

        public static CosmosDBContext CreateContext(ICosmosDBService service, bool createIfNotExists = false,
            string partitionKeyPath = null, int throughput = 0)
        {
            CosmosDBAttribute attribute = new CosmosDBAttribute(CosmosDBTestUtility.DatabaseName, CosmosDBTestUtility.CollectionName)
            {
                CreateIfNotExists = createIfNotExists,
                PartitionKey = partitionKeyPath,
                CollectionThroughput = throughput
            };

            return new CosmosDBContext
            {
                Service = service,
                ResolvedAttribute = attribute
            };
        }

        public static IOrderedQueryable<T> AsOrderedQueryable<T, TKey>(this IEnumerable<T> enumerable, Expression<Func<T, TKey>> keySelector)
        {
            return enumerable.AsQueryable().OrderBy(keySelector);
        }

        public static DocumentClientException CreateDocumentClientException(HttpStatusCode status)
        {
            Type t = typeof(DocumentClientException);

            var constructor = t.GetConstructor(
               BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic,
               null, new[] { typeof(string), typeof(Exception), typeof(HttpStatusCode?), typeof(Uri), typeof(string) }, null);

            object ex = constructor.Invoke(new object[] { string.Empty, new Exception(), status, null, string.Empty });
            return ex as DocumentClientException;
        }

        public static ParameterInfo GetInputParameter<T>()
        {
            return GetValidItemInputParameters().Where(p => p.ParameterType == typeof(T)).Single();
        }

        public static IEnumerable<ParameterInfo> GetCreateIfNotExistsParameters()
        {
            return typeof(CosmosDBTestUtility)
                .GetMethod("CreateIfNotExistsParameters", BindingFlags.Static | BindingFlags.NonPublic).GetParameters();
        }

        public static IEnumerable<ParameterInfo> GetValidOutputParameters()
        {
            return typeof(CosmosDBTestUtility)
                .GetMethod("OutputParameters", BindingFlags.Static | BindingFlags.NonPublic).GetParameters();
        }

        public static IEnumerable<ParameterInfo> GetValidItemInputParameters()
        {
            return typeof(CosmosDBTestUtility)
                 .GetMethod("ItemInputParameters", BindingFlags.Static | BindingFlags.NonPublic).GetParameters();
        }

        public static IEnumerable<ParameterInfo> GetValidClientInputParameters()
        {
            return typeof(CosmosDBTestUtility)
                 .GetMethod("ClientInputParameters", BindingFlags.Static | BindingFlags.NonPublic).GetParameters();
        }

        public static Type GetAsyncCollectorType(Type itemType)
        {
            Assembly hostAssembly = typeof(BindingFactory).Assembly;
            return hostAssembly.GetType("Microsoft.Azure.WebJobs.Host.Bindings.AsyncCollectorBinding`2")
                .MakeGenericType(itemType, typeof(CosmosDBContext));
        }

        private static void CreateIfNotExistsParameters(
            [CosmosDB("TestDB", "TestCollection", CreateIfNotExists = false)] out Item pocoOut,
            [CosmosDB("TestDB", "TestCollection", CreateIfNotExists = true)] out Item[] pocoArrayOut)
        {
            pocoOut = null;
            pocoArrayOut = null;
        }

        private static void OutputParameters(
            [CosmosDB] out object pocoOut,
            [CosmosDB] out Item[] pocoArrayOut,
            [CosmosDB] IAsyncCollector<JObject> jobjectAsyncCollector,
            [CosmosDB] ICollector<Item> pocoCollector,
            [CosmosDB] out IAsyncCollector<object> collectorOut)
        {
            pocoOut = null;
            pocoArrayOut = null;
            collectorOut = null;
        }

        private static void ItemInputParameters(
            [CosmosDB(Id = "abc123")] Document document,
            [CosmosDB(Id = "abc123")] Item poco,
            [CosmosDB(Id = "abc123")] object obj)
        {
        }

        private static void ClientInputParameters(
            [CosmosDB] DocumentClient client)
        {
        }
    }
}

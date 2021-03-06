﻿using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using Rebus.Timeout;
using System.Linq;

namespace Rebus.MongoDb
{
    /// <summary>
    /// Implementation of <see cref="IStoreTimeouts"/> that stores timeouts in a MongoDB
    /// </summary>
    public class MongoDbTimeoutStorage : IStoreTimeouts
    {
        const string ReplyToProperty = "reply_to";
        const string CorrIdProperty = "corr_id";
        const string TimeProperty = "time";
        const string SagaIdProperty = "saga_id";
        const string DataProperty = "data";
        const string IdProperty = "_id";
        readonly MongoDatabase database;
        readonly MongoCollection<BsonDocument> collection;

        /// <summary>
        /// Constructs the timeout storage, connecting to the Mongo database pointed to by the given connection string,
        /// storing the timeouts in the given collection
        /// </summary>
        public MongoDbTimeoutStorage(string connectionString, string collectionName)
        {
            database = MongoHelper.GetDatabase(connectionString);
            collection = database.GetCollection(collectionName);
            collection.EnsureIndex(IndexKeys.Ascending(TimeProperty), IndexOptions.SetBackground(true).SetUnique(false));
        }

        /// <summary>
        /// Adds the timeout to the underlying collection of timeouts
        /// </summary>
        public void Add(Timeout.Timeout newTimeout)
        {
            var doc = new BsonDocument()
                .Add(CorrIdProperty, newTimeout.CorrelationId)
                .Add(SagaIdProperty, newTimeout.SagaId)
                .Add(TimeProperty, newTimeout.TimeToReturn)
                .Add(DataProperty, newTimeout.CustomData)
                .Add(ReplyToProperty, newTimeout.ReplyTo);

            collection.Insert(doc);
        }

        /// <summary>
        /// Gets all timeouts that are due by now. Doesn't remove the timeouts or change them or anything,
        /// each individual timeout can be marked as processed by calling <see cref="DueTimeout.MarkAsProcessed"/>
        /// </summary>
        public IEnumerable<DueTimeout> GetDueTimeouts()
        {
            var result = collection.Find(Query.LTE(TimeProperty, RebusTimeMachine.Now()))
                                   .SetSortOrder(SortBy.Ascending(TimeProperty));

            return result
                .Select(r => new DueMongoTimeout(r[ReplyToProperty].AsString,
                                                 r.Contains(CorrIdProperty)
                                                     ? r[CorrIdProperty].AsString
                                                     : "",
                                                 r[TimeProperty].AsDateTime,
                                                 r.Contains(SagaIdProperty)
                                                     ? r[SagaIdProperty].AsGuid
                                                     : Guid.Empty,
                                                 r.Contains(DataProperty)
                                                     ? r[DataProperty].AsString
                                                     : "",
                                                 collection,
                                                 (ObjectId) r[IdProperty]));
        }

        class DueMongoTimeout : DueTimeout
        {
            readonly MongoCollection<BsonDocument> collection;
            readonly ObjectId objectId;

            public DueMongoTimeout(string replyTo, string correlationId, DateTime timeToReturn, Guid sagaId, string customData, MongoCollection<BsonDocument> collection, ObjectId objectId) 
                : base(replyTo, correlationId, timeToReturn, sagaId, customData)
            {
                this.collection = collection;
                this.objectId = objectId;
            }

            public override void MarkAsProcessed()
            {
                collection.Remove(Query.EQ(IdProperty, objectId));
            }
        }
    }
}
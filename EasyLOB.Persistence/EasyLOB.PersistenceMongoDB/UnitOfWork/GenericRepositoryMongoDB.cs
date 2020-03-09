﻿using EasyLOB.Data;
using EasyLOB.Library;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace EasyLOB.Persistence
{
    public abstract class GenericRepositoryMongoDB<TEntity> : IGenericRepository<TEntity>
        where TEntity : class, IZDataBase
    {
        #region Properties

        public virtual IZProfile Profile
        {
            get
            {
                Type type = this.GetRepositoryType<TEntity>();

                return DataHelper.GetProfile(type);
            }
        }

        public virtual string Entity
        {
            get { return typeof(TEntity).Name; }
        }

        public virtual int Joins
        {
            get { return 10; }
        }

        public IUnitOfWork UnitOfWork { get; }

        #endregion Properties

        #region Properties MongoDB

        public IMongoDatabase Database { get; protected set; }

        public IMongoCollection<TEntity> Collection 
        {
            get { return Database.GetCollection<TEntity>(typeof(TEntity).Name); }
        }

        #endregion Properties MongoDB

        #region Methods

        public GenericRepositoryMongoDB(IUnitOfWork unitOfWork)
        {
            UnitOfWork = unitOfWork;
        }

        public virtual int Count(Expression<Func<TEntity, bool>> where)
        {
            int result;

            Filter(where);

            if (where != null)
            {
                result = (int)Collection.CountDocuments(where);
            }
            else
            {
                result = Collection.AsQueryable<TEntity>().Count();
            }

            return result;
        }

        public virtual int Count(string where, object[] args = null)
        {
            int result;

            Filter(ref where, ref args);

            if (!string.IsNullOrEmpty(where))
            {
                if (args != null)
                {
                    result = Collection.AsQueryable<TEntity>().Where(where, args).Count();
                }
                else
                {
                    result = (int)Collection.CountDocuments(where);
                }
            }
            else
            {
                result = Collection.AsQueryable<TEntity>().Count();
            }

            return result;
        }

        public virtual int CountAll()
        {
            return Collection.AsQueryable<TEntity>().Count();
        }

        public virtual bool Create(ZOperationResult operationResult, TEntity entity)
        {
            try
            {
                if (entity.BeforeCreate(operationResult))
                {
                    if (BeforeCreate(operationResult, entity))
                    {
                        //if (UnitOfWork.BeforeCreate(operationResult, entity))
                        {
                            object id = GetNextSequence();
                            if (id != null)
                            {
                                (entity as ZDataBase).SetId(new object[] { id });
                            }

                            Collection.InsertOne(entity);

                            if (entity.AfterCreate(operationResult))
                            {
                                AfterCreate(operationResult, entity);
                                //{
                                //    UnitOfWork.AfterCreate(operationResult, entity);
                                //}
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                operationResult.ParseExceptionMongoDB(exception);
            }

            return operationResult.Ok;
        }

        public virtual bool Delete(ZOperationResult operationResult, TEntity entity)
        {
            try
            {
                if (entity.BeforeDelete(operationResult))
                {
                    if (BeforeDelete(operationResult, entity))
                    {
                        //if (UnitOfWork.BeforeDelete(operationResult, entity))
                        {
                            string predicate = Profile.LINQWhere;
                            object[] ids = entity.GetId();
                            Expression<Func<TEntity, bool>> filter = System.Linq.Dynamic.DynamicExpression.ParseLambda<TEntity, bool>(predicate, ids);

                            Collection.DeleteOne<TEntity>(filter);

                            if (entity.AfterDelete(operationResult))
                            {
                                AfterDelete(operationResult, entity);
                                //{
                                //    UnitOfWork.AfterDelete(operationResult, entity);
                                //}
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                operationResult.ParseExceptionMongoDB(exception);
            }

            return operationResult.Ok;
        }

        public virtual void Filter(Expression<Func<TEntity, bool>> where)
        {
        }

        public virtual void Filter(ref string where, ref object[] args)
        {
            if (args != null)
            {
                // DateTime
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is string)
                    {
                        // "2000-01-01T23:59:59.999Z"
                        Regex regex = new Regex(@"[0-2][0-9][0-9][0-9]-[0-1][0-9]-[0-3][0-9]T[0-2][0-9]:[0-5][0-9]:[0-5][0-9].[0-9][0-9][0-9]Z");
                        Match match = regex.Match(args[i] as string);
                        if (match.Success)
                        {
                            args[i] = DateTime.ParseExact(args[i] as string, "yyyy-MM-ddTHH:mm:ss.fffZ",
                                System.Globalization.CultureInfo.InvariantCulture).Date;
                        }
                    }
                }
            }
        }

        public virtual TEntity Get(Expression<Func<TEntity, bool>> where)
        {
            TEntity entity = null;

            Filter(where);

            entity = Query().Where(where).FirstOrDefault();
            Join(entity);

            return entity;
        }

        public virtual TEntity Get(string where, object[] args = null)
        {
            TEntity entity = null;

            Filter(ref where, ref args);

            entity = Query().Where(where).FirstOrDefault();
            Join(entity);

            return entity;
        }

        public virtual TEntity GetById(object id)
        {
            return GetById(new object[] { id });
        }

        public virtual TEntity GetById(object[] ids)
        {
            string predicate = Profile.LINQWhere;
            Expression<Func<TEntity, bool>> where =
                System.Linq.Dynamic.DynamicExpression.ParseLambda<TEntity, bool>(predicate, ids);

            return Get(where);
        }

        public virtual object[] GetIds(TEntity entity)
        {
            List<object> ids = new List<object>();

            foreach (string key in Profile.Keys)
            {
                ids.Add(LibraryHelper.GetPropertyValue(entity, key));
            }

            return ids.ToArray();
        }

        public virtual object GetNextSequence()
        {
            object id = null;

            if (Profile.IsIdentity)
            {
                string name = typeof(TEntity).Name;

                var collection = Database.GetCollection<BsonDocument>(typeof(MongoDBSequence).Name);
                var filter = Builders<BsonDocument>.Filter.Eq("Name", name);
                var update = new BsonDocument("$inc", new BsonDocument { { "Value", 1 } });
                var document = collection.FindOneAndUpdateAsync(filter, update).Result;
                if (document != null)
                {
                    MongoDBSequence mongoDBSequence = BsonSerializer.Deserialize<MongoDBSequence>(document);
                    id = mongoDBSequence.Value + 1;
                }
                else
                {
                    id = 1;
                    collection.InsertOne(new BsonDocument { { "Name", name }, { "Value", 2 } });
                }
            }

            return id;
        }

        public virtual void Join(TEntity entity)
        {
            // MongoDB Joins are client-side
            // Refer to \Application.PersistenceMongoDB\Repositories\DatabaseEntityRepositoryMongoDB.cs for Join() implementation
        }

        public virtual void Join(IEnumerable<TEntity> enumerable)
        {
            // MongoDB Joins are client-side

            if (enumerable != null)
            {
                int items = 0;
                foreach (TEntity entity in enumerable)
                {
                    Join(entity);

                    if (++items >= Joins)
                    {
                        break;
                    }
                }
            }
        }

        public virtual IQueryable<TEntity> Join(IQueryable<TEntity> query)
        {
            return query;
        }

        public virtual IQueryable<TEntity> Join(IQueryable<TEntity> query, List<Expression<Func<TEntity, object>>> associations)
        {
            return Join(query);
        }

        public virtual IQueryable<TEntity> Join(IQueryable<TEntity> query, List<string> associations)
        {
            return Join(query);
        }

        public virtual IQueryable<TEntity> Query()
        {
            IQueryable<TEntity> query = Collection.AsQueryable<TEntity>();
            query = Join(query);

            return query;
        }

        public virtual IQueryable<TEntity> Query(Expression<Func<TEntity, bool>> where = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
            int? skip = null,
            int? take = null,
            List<Expression<Func<TEntity, object>>> associations = null)
        {
            IQueryable<TEntity> query = Collection.AsQueryable<TEntity>();

            Filter(where);

            if (where != null)
            {
                query = query.Where(where);
            }

            if (skip != null && orderBy == null)
            {
                query = query.OrderBy(Profile.LINQOrderBy);
            }
            else if (orderBy != null)
            {
                query = orderBy(query);
            }

            // The method 'Skip' is only supported for sorted input in LINQ to Entities.
            // The method 'OrderBy' must be called before the method 'Skip'.
            if (skip != null && skip >= 0)
            {
                query = query.Skip((int)skip);
            }

            if (take != null && take > 0)
            {
                query = query.Take((int)take);
            }

            //query = Join(query, associations);

            return query;
        }

        public virtual IQueryable<TEntity> Query(string where = null,
            object[] args = null,
            string orderBy = null,
            int? skip = null,
            int? take = null,
            List<string> associations = null)
        {
            IQueryable<TEntity> query = Collection.AsQueryable<TEntity>();

            Filter(ref where, ref args);

            if (!string.IsNullOrEmpty(where))
            {
                if (args != null)
                {
                    query = query.Where(where, args);
                }
                else
                {
                    query = query.Where(where);
                }
            }

            //if (orderBy != null && orderBy.Contains("LookupText"))
            //{
            //    orderBy = null;
            //}

            if (skip != null && string.IsNullOrEmpty(orderBy))
            {
                query = query.OrderBy(Profile.LINQOrderBy);
            }
            else if (!string.IsNullOrEmpty(orderBy))
            {
                query = query.OrderBy(orderBy);
            }

            // The method 'Skip' is only supported for sorted input in LINQ to Entities.
            // The method 'OrderBy' must be called before the method 'Skip'.
            if (skip != null && skip >= 0)
            {
                query = query.Skip((int)skip);
            }

            if (take != null && take > 0)
            {
                query = query.Take((int)take);
            }

            //query = Join(query, associations);

            return query;
        }

        public virtual IEnumerable<TEntity> Search(Expression<Func<TEntity, bool>> where = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null,
            int? skip = null,
            int? take = null,
            List<Expression<Func<TEntity, object>>> associations = null)
        {
            IEnumerable<TEntity> enumerable = Query(where, orderBy, skip, take, associations).ToList<TEntity>();
            Join(enumerable);

            return enumerable;
        }

        public virtual IEnumerable<TEntity> Search(string where = null,
            object[] args = null,
            string orderBy = null,
            int? skip = null,
            int? take = null,
            List<string> associations = null)
        {
            IEnumerable<TEntity> enumerable = Query(where, args, orderBy, skip, take, associations).ToList<TEntity>();
            Join(enumerable);

            return enumerable;
        }

        public virtual IEnumerable<TEntity> SearchAll()
        {
            IEnumerable<TEntity> enumerable = Query().ToList<TEntity>();
            Join(enumerable);

            return enumerable;
        }

        public void SetSequence(int value)
        {
            if (Profile.IsIdentity)
            {
                string name = typeof(TEntity).Name;

                IMongoCollection<MongoDBSequence> collection = Database.GetCollection<MongoDBSequence>(typeof(MongoDBSequence).Name);
                FilterDefinition<MongoDBSequence> filter = Builders<MongoDBSequence>.Filter.Eq("Name", name);

                // UPDATE OR INSERT
                collection.UpdateOneAsync(filter,
                    Builders<MongoDBSequence>.Update.Set(x => x.Value, value),
                    new UpdateOptions { IsUpsert = true });

                // REPLACE OR INSERT
                //collection.ReplaceOneAsync(filter,
                //    new MongoDBSequence() { Name = name, Value = value },
                //    new UpdateOptions { IsUpsert = true });
            }
        }

        public virtual bool Update(ZOperationResult operationResult, TEntity entity)
        {
            try
            {
                if (entity.BeforeUpdate(operationResult))
                {
                    if (BeforeUpdate(operationResult, entity))
                    {
                        //if (UnitOfWork.BeforeUpdate(operationResult, entity))
                        {
                            string predicate = Profile.LINQWhere;
                            object[] ids = entity.GetId();
                            Expression<Func<TEntity, bool>> filter = System.Linq.Dynamic.DynamicExpression.ParseLambda<TEntity, bool>(predicate, ids);

                            Collection.ReplaceOne<TEntity>(filter, entity);

                            if (entity.AfterUpdate(operationResult))
                            {
                                AfterUpdate(operationResult, entity);
                                //{
                                //    UnitOfWork.AfterUpdate(operationResult, entity);
                                //}
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                operationResult.ParseExceptionMongoDB(exception);
            }

            return operationResult.Ok;
        }

        #endregion Methods

        #region Methods *AndSave

        public bool CreateAndSave(ZOperationResult operationResult, TEntity entity)
        {
            if (Create(operationResult, entity))
            {
                UnitOfWork.Save(operationResult);
            }

            return operationResult.Ok;
        }

        public bool DeleteAndSave(ZOperationResult operationResult, TEntity entity)
        {
            if (Delete(operationResult, entity))
            {
                UnitOfWork.Save(operationResult);
            }

            return operationResult.Ok;
        }

        public bool UpdateAndSave(ZOperationResult operationResult, TEntity entity)
        {
            if (Update(operationResult, entity))
            {
                UnitOfWork.Save(operationResult);
            }

            return operationResult.Ok;
        }

        #endregion Methods *AndSave

        #region Methods IDispose

        private bool disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                }

                disposed = true;
            }
        }

        #endregion Methods IDispose

        #region Triggers

        public virtual bool BeforeCreate(ZOperationResult operationResult, TEntity entity)
        {
            return operationResult.Ok;
        }

        public virtual bool AfterCreate(ZOperationResult operationResult, TEntity entity)
        {
            return operationResult.Ok;
        }

        public virtual bool BeforeDelete(ZOperationResult operationResult, TEntity entity)
        {
            return operationResult.Ok;
        }

        public virtual bool AfterDelete(ZOperationResult operationResult, TEntity entity)
        {
            return operationResult.Ok;
        }

        public virtual bool BeforeUpdate(ZOperationResult operationResult, TEntity entity)
        {
            return operationResult.Ok;
        }

        public virtual bool AfterUpdate(ZOperationResult operationResult, TEntity entity)
        {
            return operationResult.Ok;
        }

        #endregion Triggers
    }

    public class MongoDBSequence
    {
        [BsonId]
        public ObjectId DocumentId { get; set; }

        public string Name { get; set; }

        public int Value { get; set; }
    }
}
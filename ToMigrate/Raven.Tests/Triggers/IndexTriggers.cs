//-----------------------------------------------------------------------
// <copyright file="IndexTriggers.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System.ComponentModel.Composition.Hosting;
using System.Threading;

using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Client.Embedded;
using Raven.Json.Linq;
using Raven.Database;
using Raven.Database.Config;
using Raven.Tests.Common;
using Raven.Tests.Storage;
using Xunit;
using System.Linq;

namespace Raven.Tests.Triggers
{
    public class IndexTriggers : RavenTest
    {
        private readonly EmbeddableDocumentStore store;

        public IndexTriggers()
        {
            store = NewDocumentStore(catalog:(new TypeCatalog(typeof(IndexToDataTable))));
        }

        public override void Dispose()
        {
            store.Dispose();
            base.Dispose();
        }

        [Fact]
        public void CanReplicateValuesFromIndexToDataTable()
        {
            store.SystemDatabase.Indexes.PutIndex("test", new IndexDefinition
            {
                Map = "from doc in docs from prj in doc.Projects select new{Project = prj}",
                Stores = { { "Project", FieldStorage.Yes } }
            });
            store.SystemDatabase.Documents.Put("t", null, RavenJObject.Parse("{'Projects': ['RavenDB', 'NHibernate']}"), new RavenJObject(), null);

            QueryResult queryResult;
            do
            {
                queryResult = store.SystemDatabase.Queries.Query("test", new IndexQuery { Start = 0, PageSize = 2, Query = "Project:RavenDB" }, CancellationToken.None);
            } while (queryResult.IsStale);

            var indexToDataTable = store.SystemDatabase.IndexUpdateTriggers.OfType<IndexToDataTable>().Single();
            Assert.Equal(2, indexToDataTable.DataTable.Rows.Count);
        }
    }
}

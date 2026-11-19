using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using Dapper;

namespace AlKhayal.Data.Catalog
{
    public sealed partial class SqliteCatalogStore : ICatalogStore
    {
        private static string PosProductEligibilityPredicate(string detailsAlias, string productIdColumn)
        {
            return $@"
                {detailsAlias}.ProductType='stock' AND {detailsAlias}.MigrationState='ready' AND {detailsAlias}.IsActive=1 AND {detailsAlias}.ArchivedUtc IS NULL
                AND EXISTS (SELECT 1 FROM ProductVariants pv WHERE pv.ProductId={productIdColumn} AND pv.IsActive=1 AND pv.ArchivedUtc IS NULL)
                AND EXISTS (SELECT 1 FROM ProductUnits pu WHERE pu.ProductId={productIdColumn} AND pu.IsActive=1 AND pu.IsSaleUnit=1)
            ";
        }

        public async Task<IReadOnlyList<ProductCategory>> ListPosCategoriesAsync(CancellationToken ct)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                return (await connection.QueryAsync<ProductCategory>(new CommandDefinition(@"
                    SELECT c.Id, c.ParentCategoryId, c.Name, c.IsActive, c.SortOrder, c.CreatedUtc, c.UpdatedUtc
                    FROM ProductCategories c
                    WHERE c.IsActive=1
                      AND EXISTS (
                          SELECT 1 FROM ProductDetails d
                          WHERE d.CategoryId = c.Id
                            AND " + PosProductEligibilityPredicate("d", "d.ProductId") + @"
                      )
                    ORDER BY c.ParentCategoryId, c.SortOrder, c.Name, c.Id",
                    cancellationToken: ct)).ConfigureAwait(false)).ToList();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Catalog;
using AlKhayal.Data.Catalog;

namespace AlKhayal.IntegrationTests
{
    internal static class CustomAttributeFixture
    {
        internal static async Task<ProductAttributeAssignmentCommand> Create(string connectionString, long userId)
        {
            var store = new SqliteCatalogStore(connectionString);
            var attribute = await store.CreateAttributeAsync(new CreateAttributeRecord {
                AttributeKey = "GRADE_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Name = "درجة الجودة", Kind = "custom", IsActive = true, UserId = userId
            }, CancellationToken.None);
            var ids = new List<long>();
            foreach (var value in new[] { "A", "B" })
                ids.Add((await store.CreateAttributeValueAsync(new CreateAttributeValueRecord {
                    AttributeId = attribute.Id, Value = value, IsActive = true, UserId = userId
                }, CancellationToken.None)).Id);
            return new ProductAttributeAssignmentCommand { AttributeId = attribute.Id, ValueIds = ids };
        }
    }
}

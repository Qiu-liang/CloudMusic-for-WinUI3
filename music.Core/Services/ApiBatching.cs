using System;
using System.Collections.Generic;

namespace music.Services
{
    public static class ApiBatching
    {
        public static IReadOnlyList<IReadOnlyList<T>> Chunk<T>(IEnumerable<T> items, int batchSize)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
            var result = new List<IReadOnlyList<T>>();
            var batch = new List<T>(batchSize);
            foreach (var item in items)
            {
                batch.Add(item);
                if (batch.Count == batchSize)
                {
                    result.Add(batch);
                    batch = new List<T>(batchSize);
                }
            }
            if (batch.Count > 0) result.Add(batch);
            return result;
        }
    }
}

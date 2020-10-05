using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DankDitties
{
    public static class Utils
    {
        public static T NextWeighted<T>(this Random random, IEnumerable<T> sequence, Func<T, double> weightSelector)
        {
            double totalWeight = sequence.Sum(weightSelector);

            double itemWeightIndex = random.NextDouble() * totalWeight;
            double currentWeightIndex = 0;

            foreach (var item in from weightedItem in sequence select new { value = weightedItem, weight = weightSelector(weightedItem) })
            {
                currentWeightIndex += item.weight;

                if (currentWeightIndex >= itemWeightIndex)
                    return item.value;

            }

            return default;

        }
    }
}

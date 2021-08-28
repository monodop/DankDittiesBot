using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DankDitties
{
    public static class Utils
    {
        public static string? GetEnv(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }
        public static string GetEnv(string name, string defaultValue)
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }


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

            throw new Exception("Unable to select an item");
        }

        public static string GetUsername(this IGuildUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.Nickname))
                return user.Nickname;
            return user.Username;
        }
    }
}

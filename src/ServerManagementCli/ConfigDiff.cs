using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ServerManagementCli
{
    public class ConfigDiff
    {
        public interface IResult
        {
            Type Type { get; }
            ResultAction Action { get; }
            object? ExpectedObj { get; }
            object? ActualObj { get; }
            List<string> Changes { get; }
            List<PropertyInfo> ChangedProperties { get; }
        }
        public interface IResult<T> : IResult
        {
            T? Expected { get; }
            T? Actual { get; }
        }

        protected static class Result
        {
            public static Result<T> Added<T>(T expected) where T : class
                => new(ResultAction.Added, expected, null, new List<PropertyInfo>());

            public static Result<T> Removed<T>(T actual) where T : class
                => new(ResultAction.Removed, null, actual, new List<PropertyInfo>());

            public static Result<T> Modified<T>(T expected, T actual, List<PropertyInfo> changedProperties) where T : class
                => new(ResultAction.Modified, expected, actual, changedProperties);
        }

        protected class Result<T> : IResult<T>
            where T : class
        {
            public Type Type { get; }
            public ResultAction Action { get; }
            public object? ExpectedObj { get; }
            public object? ActualObj { get; }
            public T? Expected { get; }
            public T? Actual { get; }
            public List<string> Changes { get; }
            public List<PropertyInfo> ChangedProperties { get; }

            public Result(ResultAction action, T? expected, T? actual, List<PropertyInfo> changedProperties)
            {
                Type = typeof(T);
                Action = action;

                ExpectedObj = expected;
                Expected = expected;
                ActualObj = actual;
                Actual = actual;

                Changes = changedProperties.Select(p => p.Name).ToList();
                ChangedProperties = changedProperties;
            }

        }

        public enum ResultAction
        {
            Added,
            Removed,
            Modified,
        }

        public IEnumerable<IResult> GetDifferences(ServerConfiguration expected, ServerConfiguration actual)
        {
            var rolePairs = _fullOuterJoin(expected.Roles, actual.Roles, r => r.Id);

            foreach (var (expectedRole, actualRole) in rolePairs)
            {
                if (actualRole == null)
                {
                    // Add
                    yield return Result.Added(expectedRole);
                }
                else if (expectedRole == null)
                {
                    // Remove
                    yield return Result.Removed(actualRole);
                }
                else
                {
                    // Modify
                    List<PropertyInfo> changes = _shallowDiff(
                        expectedRole, actualRole, 
                        nameof(ServerConfigurationRole.Permissions), nameof(ServerConfigurationRole.Membership)
                    ).ToList();
                    if (_shallowDiff(expectedRole.Permissions, actualRole.Permissions).Any())
                        changes.Add(typeof(ServerConfigurationRole).GetProperty(nameof(ServerConfigurationRole.Permissions))!);
                    if (!expectedRole.Membership.SequenceEqual(actualRole.Membership))
                        changes.Add(typeof(ServerConfigurationRole).GetProperty(nameof(ServerConfigurationRole.Membership))!);

                    if (changes.Count > 0)
                        yield return Result.Modified(expectedRole, actualRole, changes);
                }
            }

            var categoryPairs = _fullOuterJoin(expected.Categories, actual.Categories, r => r.Id);
            foreach (var (expectedCategory, actualCategory) in categoryPairs)
            {
                if (actualCategory == null)
                {
                    // Add
                    yield return Result.Added(expectedCategory);
                }
                else if (expectedCategory == null)
                {
                    // Remove
                    yield return Result.Removed(actualCategory);
                }
                else
                {
                    // Modify
                    List<PropertyInfo> changes = _shallowDiff(
                        expectedCategory, actualCategory,
                        nameof(ServerConfigurationCategory.RolePermissions), nameof(ServerConfigurationCategory.UserPermissions),
                        nameof(ServerConfigurationCategory.Channels)
                    ).ToList();

                    if (changes.Count > 0)
                        yield return Result.Modified(expectedCategory, actualCategory, changes);
                }
            }

            var channelPairs = _fullOuterJoin(expected.Categories.SelectMany(c => c.Channels), actual.Categories.SelectMany(c => c.Channels), r => r.Id);
            foreach (var (expectedChannel, actualChannel) in channelPairs)
            {
                if (actualChannel == null)
                {
                    // Add
                    yield return Result.Added(expectedChannel);
                }
                else if (expectedChannel == null)
                {
                    // Remove
                    yield return Result.Removed(actualChannel);
                }
                else
                {
                    // Modify
                    List<PropertyInfo> changes = _shallowDiff(
                        expectedChannel, actualChannel,
                        nameof(ServerConfigurationChannel.RolePermissions), nameof(ServerConfigurationChannel.UserPermissions)
                    ).ToList();

                    if (changes.Count > 0)
                        yield return Result.Modified(expectedChannel, actualChannel, changes);
                }
            }
        }

        private List<(T expected, T actual)> _join<T, TProp>(List<T> expected, List<T> actual, Func<T, TProp> joinOn)
        {
            return expected.Join(actual, joinOn, joinOn, (e, a) => (e, a)).ToList();
        }

        private IEnumerable<(T a, T b)> _fullOuterJoin<T, TKey>(IEnumerable<T> a, IEnumerable<T> b, Func<T, TKey> joinOn)
        {
            var aLookup = a.ToLookup(joinOn);
            var bLookup = b.ToLookup(joinOn);

            var keys = new HashSet<TKey>(aLookup.Select(kvp => kvp.Key));
            keys.UnionWith(bLookup.Select(kvp => kvp.Key));

            return from key in keys
                   from xa in aLookup[key].DefaultIfEmpty()
                   from xb in bLookup[key].DefaultIfEmpty()
                   select (xa, xb);
        }

        private IEnumerable<PropertyInfo> _shallowDiff<T>(T expected, T actual, params string[] ignore)
        {
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (ignore.Contains(property.Name))
                    continue;

                var e = property.GetValue(expected);
                var a = property.GetValue(actual);

                if (!object.Equals(e, a))
                {
                    yield return property;
                }
            }
        }
    }
}

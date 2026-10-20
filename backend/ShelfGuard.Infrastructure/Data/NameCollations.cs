using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ShelfGuard.Infrastructure.Data;

/// <summary>
/// Database collations used explicitly in ORDER BY. The database default (en_US.utf8 on the
/// musl-based postgres:alpine image) compares by code point, which puts Latin before Cyrillic and
/// scatters the Ukrainian letters; <see cref="Ukrainian"/> is the ICU collation created by
/// migration AddUkrainianNameCollation (Ukrainian alphabet, Cyrillic before Latin).
/// </summary>
public static class NameCollations
{
    public const string Ukrainian = "uk_cyrl_first";
}

/// <summary>
/// Name ordering helpers. Each takes a plain string selector plus <c>uk</c> (true on Npgsql): the
/// selector is wrapped in <c>EF.Functions.Collate(…, "uk_cyrl_first")</c> when true, and left as a
/// normal <c>OrderBy</c> otherwise — the InMemory/Sqlite providers used by unit tests have no
/// collation support and would throw on <c>Collate</c>.
/// </summary>
public static class UkOrdering
{
    private static readonly MethodInfo CollateMethod = typeof(RelationalDbFunctionsExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(RelationalDbFunctionsExtensions.Collate)
                    && m.IsGenericMethodDefinition && m.GetParameters().Length == 3)
        .MakeGenericMethod(typeof(string));

    private static Expression<Func<T, string?>> Wrap<T>(Expression<Func<T, string?>> key) =>
        Expression.Lambda<Func<T, string?>>(
            Expression.Call(
                CollateMethod,
                Expression.Constant(EF.Functions),
                key.Body,
                Expression.Constant(NameCollations.Ukrainian)),
            key.Parameters);

    public static IOrderedQueryable<T> OrderByUk<T>(
        this IQueryable<T> query, Expression<Func<T, string?>> key, bool uk) =>
        query.OrderBy(uk ? Wrap(key) : key);

    public static IOrderedQueryable<T> OrderByDescendingUk<T>(
        this IQueryable<T> query, Expression<Func<T, string?>> key, bool uk) =>
        query.OrderByDescending(uk ? Wrap(key) : key);

    public static IOrderedQueryable<T> ThenByUk<T>(
        this IOrderedQueryable<T> query, Expression<Func<T, string?>> key, bool uk) =>
        query.ThenBy(uk ? Wrap(key) : key);

    public static IOrderedQueryable<T> ThenByDescendingUk<T>(
        this IOrderedQueryable<T> query, Expression<Func<T, string?>> key, bool uk) =>
        query.ThenByDescending(uk ? Wrap(key) : key);
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMDBScraper
{
    public static class Extensions
    {
        public static IEnumerable<T> NotNull<T>(this IEnumerable<T?> this_) where T : struct
        {
#pragma warning disable CS8629 // Nullable value type may be null.
            return this_.Where(t => t != null).Select(t => t.Value);
#pragma warning restore CS8629 // Nullable value type may be null.
        }

        public static IEnumerable<T> NotNull<T>(this IEnumerable<T?> this_) where T : class
        {
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
            return this_.Where(t => t != null);
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
        }

        public static IEnumerable<TS> SelectManyNonNull<T, TS>(this IEnumerable<T?> this_, Func<T, IEnumerable<TS>?> selector) where T : struct
        {
            return this_.NotNull().Select(selector).NotNull().SelectMany(t => t);
        }

        public static IEnumerable<TS> SelectManyNonNull<T, TS>(this IEnumerable<T?> this_, Func<T, IEnumerable<TS>?> selector) where T : class
        {
            return this_.NotNull().Select(selector).NotNull().SelectMany(t => t);
        }

        public static IEnumerable<T> NullAsEmpty<T>(this IEnumerable<T>? this_)
        {
            if (this_ == null)
                return Enumerable.Empty<T>();
            else
                return this_;
        }

        public static IEnumerable NullAsEmpty(this IEnumerable? this_)
        {
            if (this_ == null)
                return new object[0];
            else
                return this_;
        }
    }
}

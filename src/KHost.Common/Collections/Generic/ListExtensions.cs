namespace KHost.Common.Collections.Generic
{
    public static class ListExtensions
    {
        public static int FindIndex<T>(this IList<T> source, Predicate<T> match)
        {
            for (var i = 0; i < source.Count; i++)
            {
                if (!match(source[i]))
                    continue;

                return i;
            }

            return -1;
        }
    }
}

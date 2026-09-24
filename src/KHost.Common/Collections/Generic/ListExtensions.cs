namespace KHost.Common.Collections.Generic
{
    /// <summary>Fills the <c>List&lt;T&gt;.FindIndex</c> gap for the bare <see cref="IList{T}"/> interface.</summary>
    public static class ListExtensions
    {
        /// <summary>Index of the first element matching <paramref name="match"/>, or -1 when none does.</summary>
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

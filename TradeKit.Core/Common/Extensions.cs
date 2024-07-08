namespace TradeKit.Core.Common
{
    public static class Extensions
    {

        /// <summary>
        /// Adds the value to the sorted dict with list-backed value.
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedDictionary">The sorted dict.</param>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public static void AddValue<TK, TV>(
            this SortedDictionary<TK, List<TV>> sortedDictionary, TK key, TV value)
        {
            if (!sortedDictionary.TryGetValue(key, out List<TV> valList))
            {
                valList = new List<TV>();
                sortedDictionary[key] = valList;
            }

            valList.Add(value);
        }

        /// <summary>
        /// Slices the ordered array by value.
        /// </summary>
        /// <param name="inDoubles">The array of doubles.</param>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <returns>The sliced array</returns>
        public static double[] RangeVal(
            this double[] inDoubles, double startValue, double endValue)
        {
            return inDoubles
                .SkipWhile(a => a < startValue)
                .TakeWhile(a => a <= endValue)
                .ToArray();
        }

        /// <summary>
        /// Removes according to the enumerable.
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="toDeleteEnumerable">The enumerable to delete.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveWhere<TK, TV>(
            this SortedDictionary<TK, TV> sortedList, IEnumerable<TK> toDeleteEnumerable)
        {
            var keysToRemove = new List<TK>();
            foreach (TK key in toDeleteEnumerable)
            {
                keysToRemove.Add(key);
            }

            foreach (TK key in keysToRemove)
            {
                sortedList.Remove(key);
            }

            return keysToRemove.Count;
        }

        /// <summary>
        /// Removes according to the func (left part of the dictionary).
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="compareFunc">The function for comparing.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveLeft<TK, TV>(
            this SortedDictionary<TK, TV> sortedList, Func<TK, bool> compareFunc)
        {
            return sortedList.RemoveWhere(sortedList.Keys.TakeWhile(compareFunc));
        }

        /// <summary>
        /// Removes according to the func (right part of the dictionary).
        /// </summary>
        /// <typeparam name="TK">The type of the key.</typeparam>
        /// <typeparam name="TV">The type of the value.</typeparam>
        /// <param name="sortedList">The sorted list.</param>
        /// <param name="compareFunc">The function for comparing.</param>
        /// <returns>Removed items count.</returns>
        public static int RemoveRight<TK, TV>(
            this SortedDictionary<TK, TV> sortedList, Func<TK, bool> compareFunc)
        {
            return sortedList.RemoveWhere(sortedList.Keys.SkipWhile(compareFunc));
        }
    }
}

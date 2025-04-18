using System.Globalization;
using System.Text.RegularExpressions;

namespace Vapor
{
    public static class StringExtensions
    {
        public static bool EmptyOrNull(this string str)
        {
            return string.IsNullOrEmpty(str);
        }
        
         private const uint k_FnvOffsetBasis32 = 2166136261;
        private const uint k_FnvPrime32 = 16777619;
        private const ulong k_FnvOffsetBasis64 = 14695981039346656037;
        private const ulong k_FnvPrime64 = 1099511628211;

        /// <summary>
        /// non cryptographic stable hash code,  
        /// it will always return the same hash for the same
        /// string.  
        /// 
        /// This is simply an implementation of FNV-1 32 bit xor folded to 16 bit
        /// https://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function
        /// </summary>
        /// <returns>The stable hash32.</returns>
        /// <param name="txt">Text.</param>
        public static ushort GetStableHashU16(this string txt)
        {
            uint hash32 = txt.GetStableHashU32();

            return (ushort)((hash32 >> 16) ^ hash32);
        }


        /// <summary>
        /// non cryptographic stable hash code,  
        /// it will always return the same hash for the same
        /// string.  
        /// 
        /// This is simply an implementation of FNV-1 32 bit
        /// https://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function
        /// </summary>
        /// <returns>The stable hash32.</returns>
        /// <param name="txt">Text.</param>
        public static uint GetStableHashU32(this string txt)
        {
            unchecked
            {
                uint hash = k_FnvOffsetBasis32;
                for (int i = 0; i < txt.Length; i++)
                {
                    uint ch = txt[i];
                    hash *= k_FnvPrime32;
                    hash ^= ch;
                }
                return hash;
            }
        }

        /// <summary>
        /// non cryptographic stable hash code,  
        /// it will always return the same hash for the same
        /// string.  
        /// 
        /// This is simply an implementation of FNV-1  64 bit
        /// https://en.wikipedia.org/wiki/Fowler%E2%80%93Noll%E2%80%93Vo_hash_function
        /// </summary>
        /// <returns>The stable hash64.</returns>
        /// <param name="txt">Text.</param>
        public static ulong GetStableHashU64(this string txt)
        {
            unchecked
            {
                ulong hash = k_FnvOffsetBasis64;
                for (int i = 0; i < txt.Length; i++)
                {
                    ulong ch = txt[i];
                    hash *= k_FnvPrime64;
                    hash ^= ch;
                }
                return hash;
            }
        }
        
        public static string ToTitleCase(this string input)
        {
            if (input.EmptyOrNull())
            {
                return input;
            }

            // Step 1: Replace underscores with spaces
            input = input.Replace("_", " ");

            // Step 2: Insert space before uppercase letters (excluding first character)
            input = Regex.Replace(input, "(?<!^)([A-Z])", " $1");

            // Step 3: Convert to Title Case
            TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
            return textInfo.ToTitleCase(input.ToLowerInvariant());
        }
    }
}

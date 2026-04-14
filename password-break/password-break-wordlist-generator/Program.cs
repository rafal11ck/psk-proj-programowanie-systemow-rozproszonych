using System.Text;

namespace password_break_wordlist_generator
{
    internal class Program
    {
        static async Task Main()
        {
            Console.WriteLine("Start generating");

            var baseWords = new List<string>
            {
                "password", "123456", "admin", "letmein", "qwerty",
                "password123", "admin123", "test", "hello", "world"
            };

            var digitSuffixes = Enumerable.Range(0, 100).Select(i => i.ToString()).Prepend("").ToList();
            var specialSuffixes = string.Join("", Enumerable.Range(32, 95).Select(i => (char)i))
                .Where(c => !char.IsLetterOrDigit(c))
                .Select(c => c.ToString())
                .Prepend("")
                .ToList();

            var allWords = new List<string>();

            foreach (var baseWord in baseWords)
            {
                var leetCaseVariants = GenerateLeetCaseVariants(baseWord);

                foreach (var lc in leetCaseVariants)
                {
                    foreach (var ds in digitSuffixes)
                    {
                        foreach (var ss in specialSuffixes)
                        {
                            if (lc == baseWord && ds == "" && ss == "")
                                continue;

                            allWords.Add(lc + ds + ss);
                        }
                    }
                }
            }

            var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "wordlist.txt");

            await File.WriteAllLinesAsync(outputPath, allWords, Encoding.UTF8);

            Console.WriteLine($"Generated lines: {allWords.Count:N0}");
        }

        private static List<string> GenerateLeetCaseVariants(string word)
        {
            var wordLower = word.ToLower();
            var optionsList = new List<List<string>>();

            foreach (char c in wordLower)
            {
                optionsList.Add(GetReplacementOptions(c));
            }

            var leetCombinations = CartesianProduct(optionsList);
            var variants = new List<string>();

            foreach (var combo in leetCombinations)
            {
                var baseStr = string.Concat(combo);
                var letterPositions = new List<int>();

                for (int i = 0; i < baseStr.Length; i++)
                {
                    if (char.IsLetter(baseStr[i]))
                        letterPositions.Add(i);
                }

                if (letterPositions.Count == 0)
                {
                    variants.Add(baseStr);
                    continue;
                }

                int caseCombinations = 1 << letterPositions.Count;

                for (int mask = 0; mask < caseCombinations; mask++)
                {
                    var chars = baseStr.ToCharArray();
                    for (int j = 0; j < letterPositions.Count; j++)
                    {
                        if ((mask & (1 << j)) != 0)
                        {
                            int pos = letterPositions[j];
                            chars[pos] = char.ToUpper(chars[pos]);
                        }
                    }
                    variants.Add(new string(chars));
                }
            }

            return variants;
        }

        private static List<string> GetReplacementOptions(char c)
        {
            return c switch
            {
                'a' => new List<string> { "a", "@", "4" },
                'e' => new List<string> { "e", "3" },
                'i' => new List<string> { "i", "1", "!" },
                'o' => new List<string> { "o", "0" },
                's' => new List<string> { "s", "$", "5" },
                't' => new List<string> { "t", "7", "+" },
                _ => new List<string> { c.ToString() }
            };
        }

        private static IEnumerable<IEnumerable<string>> CartesianProduct(IEnumerable<IEnumerable<string>> sequences)
        {
            IEnumerable<IEnumerable<string>> emptyProduct = new[] { Enumerable.Empty<string>() };
            return sequences.Aggregate(
                emptyProduct,
                (acc, seq) =>
                    from a in acc
                    from b in seq
                    select a.Concat(new[] { b }));
        }
    }
}

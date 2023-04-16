using System;

namespace ChattyBox.Misc;

public static class ValidCharacters {
  static string[] vowels = { "a", "e", "i", "o", "u" };
  static char[] accents = { '\u0300', '\u0301', '\u0302', '\u0303', '\u0308' };

  public static string GetLetters() {
    var englishAlphabet = new List<string>();
    for (char a = 'A'; a <= 'Z'; a++) {
      var charString = a.ToString();
      englishAlphabet.Add(charString);
    }

    for (char a = 'a'; a <= 'z'; a++) {
      var charString = a.ToString();
      englishAlphabet.Add(charString);
    }

    var accentedLetters = new List<string>();

    foreach (var vowel in vowels) {
      foreach (var accent in accents) {
        accentedLetters.Add((vowel + accent).Normalize());
        accentedLetters.Add((vowel.ToUpper() + accent).Normalize());
      }
    }

    var foreignLetters = new List<string>();

    for (int i = 0; i <= 0x10FFFF; i++) {
      char c = (char)i;
      if (Char.IsLetter(c) &&
          ((c >= 0x0600 && c <= 0x06FF) ||    // Arabic and Farsi
           (c >= 0x0590 && c <= 0x05FF) ||    // Hebrew
           (c >= 0x1800 && c <= 0x18AF) ||    // Mongolian
           (c >= 0x0400 && c <= 0x04FF) ||    // Russian and Ukrainian
           (c >= 0x0370 && c <= 0x03FF) ||    // Greek
           (c >= 0x2E80 && c <= 0x2EFF) ||    // CJK Radicals Supplement and Kangxi Radicals (Chinese)
           (c >= 0x4E00 && c <= 0x9FFF) ||    // CJK Unified Ideographs (Chinese, Japanese, and Korean)
           (c >= 0xAC00 && c <= 0xD7AF)))     // Hangul Syllables (Korean)
      {
        foreignLetters.Add(c.ToString());
      }
    }

    var allowedLetters = (englishAlphabet.Concat(accentedLetters).Concat(foreignLetters)).ToList();
    allowedLetters.Add("Ç");
    allowedLetters.Add("ç");
    allowedLetters.Add(" ");
    return String.Join("", allowedLetters);
  }

}
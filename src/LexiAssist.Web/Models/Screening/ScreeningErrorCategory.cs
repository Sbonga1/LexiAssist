using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.Models.Screening;

public enum ScreeningErrorCategory
{
    [Display(Name = "Consonant blend")]
    ConsonantBlend = 1,

    [Display(Name = "Short vowel")]
    ShortVowel = 2,

    [Display(Name = "Long vowel")]
    LongVowel = 3,

    [Display(Name = "Multi-syllabic word")]
    MultiSyllabic = 4,

    [Display(Name = "Phoneme substitution")]
    PhonemeSubstitution = 5,

    [Display(Name = "Omission")]
    Omission = 6,

    [Display(Name = "Sequencing")]
    Sequencing = 7,

    [Display(Name = "Decoding")]
    Decoding = 8,

    [Display(Name = "Spelling pattern")]
    SpellingPattern = 9,

    [Display(Name = "Comprehension")]
    Comprehension = 10
}

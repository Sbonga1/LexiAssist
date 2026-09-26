using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.Models.Screening;

public enum ScreeningQuestionType
{
    [Display(Name = "Multiple choice")]
    MultipleChoice = 1,

    [Display(Name = "Text response")]
    TextResponse = 2,

    [Display(Name = "Word reading")]
    WordReading = 3,

    [Display(Name = "Non-word reading")]
    NonWordReading = 4,

    [Display(Name = "Phonological choice")]
    PhonologicalChoice = 5,

    [Display(Name = "Timed naming")]
    TimedNaming = 6,

    [Display(Name = "Spelling")]
    Spelling = 7,

    [Display(Name = "Reading comprehension")]
    ReadingPassage = 8,

    [Display(Name = "Listening comprehension")]
    ListeningResponse = 9,

    [Display(Name = "Reading fluency")]
    ReadingFluency = 10,

    [Display(Name = "Working memory sequence")]
    WorkingMemorySequence = 11,

    [Display(Name = "Timed matching")]
    TimedMatching = 12,

    [Display(Name = "Pattern reasoning")]
    PatternReasoning = 13
}

using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.ViewModels.AdminUsers;

public enum ManagedUserRole
{
    [Display(Name = "Learner")]
    Learner = 1,

    [Display(Name = "Psychologist")]
    Psychologist = 2
}

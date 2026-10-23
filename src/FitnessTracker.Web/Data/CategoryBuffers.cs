namespace FitnessTracker.Web.Data;

/// <summary>
/// ACC-6: a person's default getting-ready and changing time for one category of activity. An activity whose buffers
/// are left blank gets these. Until the person sets their own, <see cref="BuiltIn"/> applies.
/// </summary>
public class CategoryBuffers : IOwnedByUser, IVersioned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string OwnerId { get; set; } = "";

    public Guid Version { get; set; }

    public ActivityCategory Category { get; set; }

    public int PreparationMinutes { get; set; }

    public int ChangingMinutes { get; set; }

    /// <summary>
    /// The defaults before a person sets their own: some time to get ready and to change or shower for exercise
    /// (10 minutes' changing, as in requirements T1), and none for anything else.
    /// </summary>
    public static (int Preparation, int Changing) BuiltIn(ActivityCategory category) => category switch
    {
        ActivityCategory.Gym => (10, 10),
        ActivityCategory.Run or ActivityCategory.OtherExercise => (5, 10),
        _ => (0, 0),
    };
}

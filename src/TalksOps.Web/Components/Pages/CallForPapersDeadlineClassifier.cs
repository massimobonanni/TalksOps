namespace TalksOps.Web.Components.Pages;

public readonly record struct CallForPapersDeadlineStatus(string ColorClass, string RemainingLabel);

public static class CallForPapersDeadlineClassifier
{
    public static CallForPapersDeadlineStatus Classify(DateOnly deadline, DateOnly today)
    {
        var daysRemaining = deadline.DayNumber - today.DayNumber;

        if (daysRemaining < 0)
        {
            return new("deadline-expired", "Expired");
        }

        var colorClass = daysRemaining < 1
            ? "deadline-urgent"
            : daysRemaining < 7
                ? "deadline-soon"
                : "deadline-upcoming";
        var remainingLabel = daysRemaining == 1 ? "1 day" : $"{daysRemaining} days";

        return new(colorClass, remainingLabel);
    }
}
namespace TalksOps.Core.Domain;

/// <summary>Contains validation rules for event data.</summary>
public static class EventRules
{
    /// <summary>Returns validation errors for an event, or an empty list when valid.</summary>
    public static IReadOnlyList<string> Validate(Event value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(value.OwnerId))
        {
            errors.Add("An owner is required.");
        }

        if (string.IsNullOrWhiteSpace(value.Name))
        {
            errors.Add("An event name is required.");
        }

        if (string.IsNullOrWhiteSpace(value.Location))
        {
            errors.Add("An event location is required.");
        }

        if (value.EndDate < value.StartDate)
        {
            errors.Add("The event end date must be on or after its start date.");
        }

        foreach (var (costName, amount) in value.Costs)
        {
            if (string.IsNullOrWhiteSpace(costName))
            {
                errors.Add("Cost names cannot be empty.");
            }

            if (amount < 0)
            {
                errors.Add($"Cost '{costName}' cannot be negative.");
            }
        }

        return errors;
    }
}
using System.Reflection;

namespace FitnessTracker.Domain.Tests;

public class ArchitectureTests
{
    // Libraries the domain may use besides the .NET base library. Adding one here is a deliberate
    // decision: check first that it doesn't bring ASP.NET Core or EF Core with it.
    private static readonly string[] ApprovedLibraries = [];

    // CLAUDE.md architecture rule 1: the domain stays free of web and database frameworks, so the
    // scheduling rules can be tested on their own and reused by every way of saving.
    // An allow-list rather than a block-list, because parts of ASP.NET Core live in assemblies that
    // don't start with "Microsoft.AspNetCore" (Identity is Microsoft.Extensions.Identity.*).
    // The compiler only records references the code actually uses, so an unused package reference
    // isn't caught; any code that touches it is.
    [Fact]
    public void Domain_UsesOnlyTheBaseLibraryAndApprovedLibraries()
    {
        var domain = Assembly.Load("FitnessTracker.Domain");

        var unapproved = domain
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "")
            .Where(name => !IsBaseLibrary(name) && !ApprovedLibraries.Contains(name))
            .ToList();

        Assert.Empty(unapproved);
    }

    private static bool IsBaseLibrary(string name) =>
        name is "System" or "netstandard" || name.StartsWith("System.", StringComparison.Ordinal);
}

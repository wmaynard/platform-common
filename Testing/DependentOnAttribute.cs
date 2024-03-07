using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.Common.Testing;

[AttributeUsage(validOn: AttributeTargets.Class)]
public class DependentOnAttribute : Attribute
{
    public readonly Type[] Dependencies;
    public DependentOnAttribute(params Type[] tests)
    {
        if (!tests.All(type => type.IsAssignableTo(typeof(PlatformUnitTest))))
            throw new PlatformException($"DependentOn type must be a {nameof(PlatformUnitTest)}.");
        Dependencies = tests;
    }
}
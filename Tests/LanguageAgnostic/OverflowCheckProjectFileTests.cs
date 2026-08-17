using System.Linq;
using System.Xml.Linq;
using ICSharpCode.CodeConverter.CSharp;
using Xunit;

namespace ICSharpCode.CodeConverter.Tests.LanguageAgnostic;

/// <summary>
/// VB and C# default integer overflow checking the OPPOSITE way, and a project that never
/// mentions the setting inherits its language's default silently.
///
/// VB `RemoveIntegerChecks` defaults to false - arithmetic is CHECKED, and an overflowing
/// Integer multiply throws OverflowException. C# `CheckForOverflowUnderflow` defaults to false
/// - arithmetic is UNCHECKED, and the same multiply silently wraps.
///
/// So converting a project that says nothing used to trade a loud exception for a quietly
/// wrong number, across the whole assembly, with nothing to show for it: not a compile error,
/// no change to the metadata surface. It was only visible by decompiling both assemblies and
/// noticing the checked arithmetic had gone - 648 sites to 0, on the codebase this was found on.
/// </summary>
public class OverflowCheckProjectFileTests
{
    /// <summary>
    /// Mirrors the per-PropertyGroup loop in <c>VBToCSConversion.PostTransformProjectFile</c>.
    /// That method is not called directly here because its other steps need a fully constructed
    /// conversion (project contents converter, language version); this isolates the one step.
    /// </summary>
    private static string Convert(string projectXml)
    {
        var xmlDoc = XDocument.Parse(projectXml);
        XNamespace xmlNs = xmlDoc.Root.GetDefaultNamespace();
        foreach (var propertyGroup in xmlDoc.Descendants(xmlNs + "PropertyGroup").ToList()) {
            VBToCSConversion.TweakOverflowChecks(propertyGroup, xmlNs);
        }
        return xmlDoc.ToString();
    }

    /// <summary>The case that bit: VB silent, so VB-checked, so C# must say so explicitly.</summary>
    [Fact]
    public void SilentVbProjectBecomesExplicitlyChecked()
    {
        var converted = Convert(@"<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup Condition="" '$(Configuration)' == 'Release' "">
    <OutputPath>bin\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
  </PropertyGroup>
</Project>");

        Assert.Contains("<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>", converted);
    }

    [Fact]
    public void RemoveIntegerChecksFalseBecomesChecked()
    {
        var converted = Convert(@"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup Condition="" '$(Configuration)' == 'Debug' "">
    <OutputPath>bin\</OutputPath>
    <RemoveIntegerChecks>false</RemoveIntegerChecks>
  </PropertyGroup>
</Project>");

        Assert.Contains("<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>", converted);
        Assert.DoesNotContain("RemoveIntegerChecks", converted);
    }

    /// <summary>The one case where unchecked is correct - VB explicitly asked for it.</summary>
    [Fact]
    public void RemoveIntegerChecksTrueBecomesUnchecked()
    {
        var converted = Convert(@"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup Condition="" '$(Configuration)' == 'Release' "">
    <OutputPath>bin\</OutputPath>
    <RemoveIntegerChecks>true</RemoveIntegerChecks>
  </PropertyGroup>
</Project>");

        Assert.Contains("<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>", converted);
        Assert.DoesNotContain("RemoveIntegerChecks", converted);
    }

    /// <summary>
    /// Written per configuration, because VB allows the setting to differ per configuration -
    /// commonly checked in Debug and unchecked in Release.
    /// </summary>
    [Fact]
    public void EachConfigurationKeepsItsOwnSetting()
    {
        var converted = Convert(@"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup Condition="" '$(Configuration)' == 'Debug' "">
    <OutputPath>bin\</OutputPath>
    <RemoveIntegerChecks>false</RemoveIntegerChecks>
  </PropertyGroup>
  <PropertyGroup Condition="" '$(Configuration)' == 'Release' "">
    <OutputPath>bin\</OutputPath>
    <RemoveIntegerChecks>true</RemoveIntegerChecks>
  </PropertyGroup>
</Project>");

        Assert.Contains("<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>", converted);
        Assert.Contains("<CheckForOverflowUnderflow>false</CheckForOverflowUnderflow>", converted);
    }

    /// <summary>
    /// Only configuration groups get it. A bare group of assembly metadata is not where
    /// compiler settings live, and adding it there would be noise in every converted project.
    /// </summary>
    [Fact]
    public void NonConfigurationPropertyGroupsAreLeftAlone()
    {
        var converted = Convert(@"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <RootNamespace>Foo</RootNamespace>
    <AssemblyName>Foo</AssemblyName>
  </PropertyGroup>
</Project>");

        Assert.DoesNotContain("CheckForOverflowUnderflow", converted);
    }
}

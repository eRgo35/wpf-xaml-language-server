# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.2.0] - first release of the eRgo35 fork

### Added

- **Find references** (`textDocument/references`): resource keys, `x:Name` (with `ElementName`, `TargetName`,
  `SourceName` and `x:Reference` uses, per namescope, and uses in the `x:Class` code-behind), types and members
  across the solution's XAML.
- **Rename** (`textDocument/rename`, `prepareRename`) for `x:Key` and `x:Name`, including the code-behind.
- **Formatting** (`textDocument/formatting`): indentation and attribute spacing only.
- **Document symbols** (`textDocument/documentSymbol`): the element tree.
- A release workflow that attaches a framework-dependent `wpf-xaml-ls-<version>.zip` to each GitHub release.

### Fixed

- The WPF targeting pack wasn't found when `DOTNET_CLI_HOME` moves the NuGet cache, so completion was empty on such
  machines (common on Linux).
- A crash when walking up from a relative document path to look for a project file.

### Changed

- StreamJsonRpc 2.26.150 and Nerdbank.Streams 2.14.411. This also fixes the MessagePack advisory
  GHSA-qhrr-8q5h-9q3h that failed the default build.

## [0.1.0-alpha] - first public version

### Added

- **Parsing:** an error-tolerant XAML parser and markup-extension parser with exact source spans.
- **Type system:** WPF types over Roslyn compilations: `XmlnsDefinition` and `clr-namespace` namespaces, attached
  properties and events, routed events, content properties, markup extensions. No WPF runtime is needed.
- **Diagnostics:**
  - unknown types, members, directives, markup extensions and prefixes, with suggestions;
  - invalid enum and Boolean values;
  - read-only properties;
  - duplicate names;
  - missing event handlers;
  - unresolved assemblies.
- **Resources:**
  - lookup through element resources, `ResourceDictionary`, `MergedDictionaries` and `Source` files (relative,
    rooted and pack URIs, across projects), and `App.xaml`, including the applications that reference a library;
  - missing-key (`XLS1014`) and missing-dictionary (`XLS1013`) warnings.
- **Bindings:**
  - data context inference from `d:DataContext`, `DataTemplate DataType`, `ItemsSource` items and `DataContext`
    declarations;
  - `ElementName`, `RelativeSource` and `Source`;
  - binding path (`XLS1015`) and `TemplateBinding` (`XLS1016`) warnings, reported only when certain.
- **Editor features:**
  - completion for elements, attributes, values, markup extensions, resource keys, binding paths and closing tags;
  - hover with XML documentation;
  - go to definition into C#, other XAML files and binding paths.
- **Project system:**
  - MSBuildWorkspace loading;
  - exact XAML ownership from `Page` items, with excluded files getting syntax checks only (`XLS9001`);
  - on-demand loading of projects outside the solution;
  - built DLLs for up-to-date project references;
  - a generator-free first compilation for fast first diagnostics.
- **Tools and editors:**
  - the `wpf-xaml-ls check` command for CI and agents;
  - Neovim configuration (`editors/nvim`), tested next to lemminx.

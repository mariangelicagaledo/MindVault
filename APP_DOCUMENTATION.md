# MindVault App Documentation

## Table of Contents
1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Navigation System](#navigation-system)
4. [Pages](#pages)
5. [Custom Controls](#custom-controls)
6. [Services](#services)
7. [Development Guidelines](#development-guidelines)
8. [Troubleshooting](#troubleshooting)

## Overview

MindVault is a .NET MAUI application designed for managing reviewers and courses. It provides a comprehensive system for creating, editing, and reviewing educational content through flashcards and interactive learning experiences.

### Key Features
- **Reviewer Management**: Create and edit reviewers with custom titles
- **Flashcard System**: Interactive learning with front/back card flipping
- **Progress Tracking**: Monitor learning progress with statistics
- **Import/Export**: Share and backup reviewer content
- **Settings Management**: Customize learning modes and preferences

## Architecture

### Technology Stack
- **Framework**: .NET MAUI 9.0
- **UI**: XAML with C# code-behind
- **Navigation**: MAUI Shell Navigation
- **Platforms**: Windows, macOS, iOS, Android

### Project Structure
```
mindvault/
├── Controls/                 # Custom UI controls
├── Pages/                   # Application pages
├── Services/                # Business logic services
├── AppShell.xaml           # Main navigation shell
├── AppShell.xaml.cs        # Shell configuration
└── App.xaml                # Application entry point
```

## Navigation System

### Overview
The app uses a centralized navigation system built on MAUI Shell with custom controls for consistent user experience across all platforms.

### Key Components

#### 1. NavigationService
**Location**: `Services/NavigationService.cs`

Central service providing navigation methods for all pages:
```csharp
public static class NavigationService
{
    public static Task Go(string route) => Shell.Current.GoToAsync(route);
    public static Task Back() => Shell.Current.GoToAsync("..");
    public static Task ToRoot() => Shell.Current.GoToAsync($"//{nameof(Pages.ReviewersPage)}");
    
    // Page-specific navigation methods
    public static Task OpenImport() => Go(nameof(Pages.ImportPage));
    public static Task OpenExport() => Go(nameof(Pages.ExportPage));
    public static Task OpenTitle() => Go(nameof(Pages.TitleReviewerPage));
    // ... more methods
}
```

#### 2. Shell Routes
**Location**: `AppShell.xaml.cs`

All navigation routes are registered in the AppShell constructor:
```csharp
public AppShell()
{
    InitializeComponent();
    
    // Register routes for navigation
    Routing.RegisterRoute(nameof(Pages.TitleReviewerPage), typeof(Pages.TitleReviewerPage));
    Routing.RegisterRoute(nameof(Pages.ReviewersPage), typeof(Pages.ReviewersPage));
    Routing.RegisterRoute(nameof(Pages.ReviewerEditorPage), typeof(Pages.ReviewerEditorPage));
    Routing.RegisterRoute(nameof(Pages.CourseReviewPage), typeof(Pages.CourseReviewPage));
    Routing.RegisterRoute(nameof(Pages.ReviewerSettingsPage), typeof(Pages.ReviewerSettingsPage));
    Routing.RegisterRoute(nameof(Pages.ImportPage), typeof(Pages.ImportPage));
    Routing.RegisterRoute(nameof(Pages.ExportPage), typeof(Pages.ExportPage));
}
```

### Navigation Flow
```   
ReviewersPage (Root)
├── TitleReviewerPage
│   └── ReviewerEditorPage
├── CourseReviewPage
│   └── ReviewerSettingsPage
├── ImportPage
└── ExportPage
```

## Pages

### 1. ReviewersPage
**File**: `Pages/ReviewersPage.xaml(.cs)`
**Purpose**: Main landing page displaying all available reviewers
**Key Features**:
- List of reviewer cards with progress indicators
- Sort options (All, Last Played)
- Hamburger menu for navigation
- "VIEW COURSE" button for each reviewer

**Navigation**:
- Hamburger menu → Import/Export/Title pages
- VIEW COURSE → CourseReviewPage

### 2. TitleReviewerPage
**File**: `Pages/TitleReviewerPage.xaml(.cs)`
**Purpose**: Create new reviewers with custom titles
**Key Features**:
- Title input field
- Create button
- Hamburger menu

**Navigation**:
- Create button → ReviewerEditorPage
- Hamburger menu → Import/Export pages

### 3. ReviewerEditorPage
**File**: `Pages/ReviewerEditorPage.xaml(.cs)`
**Purpose**: Edit reviewer content (questions and answers)
**Key Features**:
- Add/edit questions and answers
- Save/delete functionality
- Check button to return

**Navigation**:
- Check button → TitleReviewerPage
- Hamburger menu → Import/Export pages

### 4. CourseReviewPage
**File**: `Pages/CourseReviewPage.xaml(.cs)`
**Purpose**: Interactive flashcard learning experience
**Key Features**:
- Flashcard display (front/back)
- Progress tracking
- Learning statistics
- Settings access

**Navigation**:
- X button → ReviewersPage
- Settings icon → ReviewerSettingsPage
- Hamburger menu → Import/Export/Title pages

### 5. ReviewerSettingsPage
**File**: `Pages/ReviewerSettingsPage.xaml(.cs)`
**Purpose**: Configure learning preferences and modes
**Key Features**:
- Learning mode selection
- Questions per round settings
- Hamburger menu

**Navigation**:
- X button → CourseReviewPage
- Hamburger menu → Import/Export/Title pages

### 6. ImportPage
**File**: `Pages/ImportPage.xaml(.cs)`
**Purpose**: Import reviewer content from external sources
**Key Features**:
- Import functionality
- Back navigation

**Navigation**:
- X button → Back to previous page
- Hamburger menu → Import/Export/Title pages

### 7. ExportPage
**File**: `Pages/ExportPage.xaml(.cs)`
**Purpose**: Export reviewer content to external formats
**Key Features**:
- Export functionality
- Back navigation

**Navigation**:
- X/Back buttons → Back to previous page
- Hamburger menu → Import/Export/Title pages

## Custom Controls

### 1. HamburgerButton
**File**: `Controls/HamburgerButton.xaml(.cs)`
**Purpose**: Reusable hamburger menu button with animation

**Features**:
- Tap animation (scale effect)
- Clicked event
- Command/CommandParameter support
- Consistent styling across pages

**Usage**:
```xml
<controls:HamburgerButton x:Name="HamburgerButton" />
```

### 2. BottomSheetMenu
**File**: `Controls/BottomSheetMenu.xaml(.cs)`
**Purpose**: Bottom sheet menu with navigation options

**Features**:
- Smooth slide-up animation
- Overlay backdrop
- Menu item events
- Command support for dynamic content

**Usage**:
```xml
<controls:BottomSheetMenu x:Name="MainMenu" Grid.RowSpan="2" ZIndex="50"/>
```

**Menu Items**:
- Create (Title Reviewer)
- Browse (Reviewers)
- Import
- Export

## Services

### NavigationService
**File**: `Services/NavigationService.cs`
**Purpose**: Centralized navigation logic

**Key Methods**:
- `Go(route)`: Navigate to specific route
- `Back()`: Navigate back one level
- `ToRoot()`: Navigate to root page
- Page-specific navigation methods

## Development Guidelines

### Adding New Pages

1. **Create XAML and Code-behind**:
   ```csharp
   // Pages/NewPage.xaml.cs
   public partial class NewPage : ContentPage
   {
       public NewPage()
       {
           InitializeComponent();
           // Setup navigation
       }
   }
   ```

2. **Register Route** in `AppShell.xaml.cs`:
   ```csharp
   Routing.RegisterRoute(nameof(Pages.NewPage), typeof(Pages.NewPage));
   ```

3. **Add Navigation Method** in `NavigationService.cs`:
   ```csharp
   public static Task OpenNewPage() => Go(nameof(Pages.NewPage));
   ```

### Navigation Patterns

1. **Use NavigationService** for all navigation:
   ```csharp
   await NavigationService.OpenImport();
   ```

2. **Add Debug Logging** for troubleshooting:
   ```csharp
   Debug.WriteLine($"[PageName] Action() -> TargetPage");
   ```

3. **Handle Menu Actions** consistently:
   ```csharp
   MainMenu.CreateTapped += async (_, __) => await NavigationService.OpenTitle();
   MainMenu.ImportTapped += async (_, __) => await NavigationService.OpenImport();
   MainMenu.ExportTapped += async (_, __) => await NavigationService.OpenExport();
   ```

### UI Guidelines

1. **Consistent Styling**: Use predefined styles from Resources
2. **Responsive Design**: Test on multiple screen sizes
3. **Accessibility**: Ensure proper contrast and touch targets
4. **Platform Consistency**: Maintain consistent behavior across platforms

## Troubleshooting

### Common Issues

#### 1. Navigation Not Working
- Check if route is registered in `AppShell.xaml.cs`
- Verify NavigationService method exists
- Ensure proper async/await usage

#### 2. Build Errors
- Verify all using statements are present
- Check for missing dependencies
- Ensure XAML syntax is correct

#### 3. Runtime Issues
- Check Debug output for navigation logs
- Verify event handlers are properly wired
- Test on target platform

### Debug Tips

1. **Enable Debug Logging**: All navigation actions log to Debug output
2. **Check Shell State**: Verify Shell.Current is available
3. **Test Navigation Flow**: Use debugger to step through navigation calls

### Performance Considerations

1. **Lazy Loading**: Load page content only when needed
2. **Memory Management**: Dispose of event handlers properly
3. **UI Thread**: Ensure UI updates happen on main thread

## Testing

### Manual Testing Checklist

- [ ] All navigation buttons work correctly
- [ ] Hamburger menus open and close properly
- [ ] Back navigation returns to correct page
- [ ] Menu items navigate to intended destinations
- [ ] Progress tracking updates correctly
- [ ] Settings persist across navigation

### Platform Testing

- [ ] Windows (Desktop)
- [ ] macOS
- [ ] iOS Simulator
- [ ] Android Emulator

## Deployment

### Build Configuration

1. **Release Mode**: Use `dotnet build -c Release`
2. **Platform Targeting**: Build for specific platforms as needed
3. **Code Signing**: Ensure proper certificates for distribution

### Distribution

1. **Windows**: MSIX package or executable
2. **macOS**: DMG or App bundle
3. **iOS**: IPA through App Store
4. **Android**: APK or AAB through Play Store

## Support

### Getting Help

1. **Documentation**: Refer to this document first
2. **Code Comments**: Check inline documentation
3. **Debug Logs**: Review navigation flow logs
4. **Platform Issues**: Check MAUI documentation

### Contributing

1. **Follow Patterns**: Use existing navigation and UI patterns
2. **Add Documentation**: Update this document for new features
3. **Test Thoroughly**: Verify functionality across platforms
4. **Code Review**: Ensure consistency with existing codebase

---

**Last Updated**: [Current Date]
**Version**: 1.0
**Maintainer**: Development Team

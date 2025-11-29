using FluentAssertions;
using OneNoteMarkdownExporter.Models;
using Xunit;

namespace OneNoteMarkdownExporter.Tests.Models;

/// <summary>
/// Tests for OneNoteItem model and property change notifications.
/// </summary>
public class OneNoteItemTests
{
    #region Default Value Tests

    [Fact]
    public void Id_DefaultsToEmptyString()
    {
        // Arrange & Act
        var item = new OneNoteItem();

        // Assert
        item.Id.Should().BeEmpty();
    }

    [Fact]
    public void Name_DefaultsToEmptyString()
    {
        // Arrange & Act
        var item = new OneNoteItem();

        // Assert
        item.Name.Should().BeEmpty();
    }

    [Fact]
    public void Children_InitializesAsEmptyList()
    {
        // Arrange & Act
        var item = new OneNoteItem();

        // Assert
        item.Children.Should().NotBeNull();
        item.Children.Should().BeEmpty();
    }

    [Fact]
    public void IsSelected_DefaultsToFalse()
    {
        // Arrange & Act
        var item = new OneNoteItem();

        // Assert
        item.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void IsExpanded_DefaultsToFalse()
    {
        // Arrange & Act
        var item = new OneNoteItem();

        // Assert
        item.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void Path_DefaultsToEmptyString()
    {
        // Arrange & Act
        var item = new OneNoteItem();

        // Assert
        item.Path.Should().BeEmpty();
    }

    #endregion

    #region Property Setting Tests

    [Fact]
    public void Id_CanBeSet()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Id = "test-id-123";

        // Assert
        item.Id.Should().Be("test-id-123");
    }

    [Fact]
    public void Name_CanBeSet()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Name = "My Notebook";

        // Assert
        item.Name.Should().Be("My Notebook");
    }

    [Fact]
    public void Path_CanBeSet()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Path = @"C:\Export\MyNotebook";

        // Assert
        item.Path.Should().Be(@"C:\Export\MyNotebook");
    }

    #endregion

    #region Type Tests

    [Fact]
    public void Type_SetsNotebook()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Type = OneNoteItemType.Notebook;

        // Assert
        item.Type.Should().Be(OneNoteItemType.Notebook);
    }

    [Fact]
    public void Type_SetsSectionGroup()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Type = OneNoteItemType.SectionGroup;

        // Assert
        item.Type.Should().Be(OneNoteItemType.SectionGroup);
    }

    [Fact]
    public void Type_SetsSection()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Type = OneNoteItemType.Section;

        // Assert
        item.Type.Should().Be(OneNoteItemType.Section);
    }

    [Fact]
    public void Type_SetsPage()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Type = OneNoteItemType.Page;

        // Assert
        item.Type.Should().Be(OneNoteItemType.Page);
    }

    #endregion

    #region Property Change Notification Tests

    [Fact]
    public void IsSelected_RaisesPropertyChanged()
    {
        // Arrange
        var item = new OneNoteItem();
        var propertyChanged = false;
        item.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(OneNoteItem.IsSelected))
                propertyChanged = true;
        };

        // Act
        item.IsSelected = true;

        // Assert
        propertyChanged.Should().BeTrue();
    }

    [Fact]
    public void IsExpanded_RaisesPropertyChanged()
    {
        // Arrange
        var item = new OneNoteItem();
        var propertyChanged = false;
        item.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(OneNoteItem.IsExpanded))
                propertyChanged = true;
        };

        // Act
        item.IsExpanded = true;

        // Assert
        propertyChanged.Should().BeTrue();
    }

    [Fact]
    public void IsSelected_DoesNotRaisePropertyChangedWhenValueUnchanged()
    {
        // Arrange
        var item = new OneNoteItem();
        item.IsSelected = true;
        var propertyChanged = false;
        item.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(OneNoteItem.IsSelected))
                propertyChanged = true;
        };

        // Act - set to same value
        item.IsSelected = true;

        // Assert
        propertyChanged.Should().BeFalse();
    }

    [Fact]
    public void IsExpanded_DoesNotRaisePropertyChangedWhenValueUnchanged()
    {
        // Arrange
        var item = new OneNoteItem();
        item.IsExpanded = true;
        var propertyChanged = false;
        item.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(OneNoteItem.IsExpanded))
                propertyChanged = true;
        };

        // Act - set to same value
        item.IsExpanded = true;

        // Assert
        propertyChanged.Should().BeFalse();
    }

    #endregion

    #region Static Selection Event Tests

    [Fact]
    public void IsSelected_RaisesStaticSelectionChangedEvent()
    {
        // Arrange
        var item = new OneNoteItem();
        OneNoteItem? changedItem = null;
        EventHandler handler = (sender, args) => changedItem = sender as OneNoteItem;
        OneNoteItem.SelectionChanged += handler;

        try
        {
            // Act
            item.IsSelected = true;

            // Assert
            changedItem.Should().Be(item);
        }
        finally
        {
            // Cleanup - unsubscribe handler
            OneNoteItem.SelectionChanged -= handler;
        }
    }

    [Fact]
    public void IsSelected_SelectionChangedEventPassesSenderCorrectly()
    {
        // Arrange
        var item1 = new OneNoteItem { Id = "item1" };
        var item2 = new OneNoteItem { Id = "item2" };
        var senderIds = new List<string>();
        EventHandler handler = (sender, args) =>
        {
            if (sender is OneNoteItem item)
                senderIds.Add(item.Id);
        };
        OneNoteItem.SelectionChanged += handler;

        try
        {
            // Act
            item1.IsSelected = true;
            item2.IsSelected = true;

            // Assert
            senderIds.Should().Contain("item1");
            senderIds.Should().Contain("item2");
        }
        finally
        {
            OneNoteItem.SelectionChanged -= handler;
        }
    }

    #endregion

    #region Children Collection Tests

    [Fact]
    public void Children_CanAddItem()
    {
        // Arrange
        var parent = new OneNoteItem { Name = "Parent" };
        var child = new OneNoteItem { Name = "Child" };

        // Act
        parent.Children.Add(child);

        // Assert
        parent.Children.Should().Contain(child);
    }

    [Fact]
    public void Children_CanContainMultipleItems()
    {
        // Arrange
        var parent = new OneNoteItem { Name = "Parent" };
        var child1 = new OneNoteItem { Name = "Child 1" };
        var child2 = new OneNoteItem { Name = "Child 2" };
        var child3 = new OneNoteItem { Name = "Child 3" };

        // Act
        parent.Children.Add(child1);
        parent.Children.Add(child2);
        parent.Children.Add(child3);

        // Assert
        parent.Children.Should().HaveCount(3);
    }

    [Fact]
    public void Children_CanBeCleared()
    {
        // Arrange
        var parent = new OneNoteItem { Name = "Parent" };
        parent.Children.Add(new OneNoteItem { Name = "Child 1" });
        parent.Children.Add(new OneNoteItem { Name = "Child 2" });

        // Act
        parent.Children.Clear();

        // Assert
        parent.Children.Should().BeEmpty();
    }

    #endregion

    #region Object Initializer Tests

    [Fact]
    public void ObjectInitializer_SetsAllProperties()
    {
        // Arrange & Act
        var item = new OneNoteItem
        {
            Id = "notebook-123",
            Name = "My Notebook",
            Type = OneNoteItemType.Notebook,
            Path = @"C:\Export",
            IsSelected = true,
            IsExpanded = true
        };

        // Assert
        item.Id.Should().Be("notebook-123");
        item.Name.Should().Be("My Notebook");
        item.Type.Should().Be(OneNoteItemType.Notebook);
        item.Path.Should().Be(@"C:\Export");
        item.IsSelected.Should().BeTrue();
        item.IsExpanded.Should().BeTrue();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Name_HandlesSpecialCharacters()
    {
        // Arrange
        var item = new OneNoteItem();

        // Act
        item.Name = "Test <Name> & \"Quotes\"";

        // Assert
        item.Name.Should().Be("Test <Name> & \"Quotes\"");
    }

    [Fact]
    public void Name_HandlesEmptyString()
    {
        // Arrange
        var item = new OneNoteItem { Name = "Initial" };

        // Act
        item.Name = "";

        // Assert
        item.Name.Should().BeEmpty();
    }

    [Fact]
    public void Path_HandlesLongPath()
    {
        // Arrange
        var item = new OneNoteItem();
        var longPath = @"C:\Very\Long\Path\" + new string('x', 200);

        // Act
        item.Path = longPath;

        // Assert
        item.Path.Should().Be(longPath);
    }

    #endregion
}

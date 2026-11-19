using System.Windows.Controls;

namespace PiggyHelper.Pages;

public partial class ComingSoonPage : UserControl
{
    public ComingSoonPage(string title, string description)
    {
        InitializeComponent();
        FeatureTitle.Text = title;
        FeatureDesc.Text = description;
    }
}

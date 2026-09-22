using System.Windows;
using System.Windows.Controls;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public partial class ManageProfilesDialog : Window
{
    private PersonalProfile? _editing;

    public ManageProfilesDialog()
    {
        InitializeComponent();
        ProfileList.ItemsSource = PersonalProfileStore.All;
        if (PersonalProfileStore.All.Count > 0)
            ProfileList.SelectedIndex = 0;
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is PersonalProfile p)
            LoadIntoForm(p);
        else
            ClearForm();
    }

    private void LoadIntoForm(PersonalProfile p)
    {
        _editing = p;
        DisplayNameBox.Text = p.DisplayName;
        FirstNameBox.Text   = p.FirstName;
        MiddleNameBox.Text  = p.MiddleName;
        LastNameBox.Text    = p.LastName;
        DobBox.Text         = p.DateOfBirth;
        NiBox.Text          = p.NationalInsuranceNumber;
        EmailBox.Text       = p.Email;
        PhoneBox.Text       = p.Phone;
        Addr1Box.Text       = p.AddressLine1;
        Addr2Box.Text       = p.AddressLine2;
        CityBox.Text        = p.City;
        CountyBox.Text      = p.County;
        PostcodeBox.Text    = p.Postcode;
        CountryBox.Text     = p.Country;

        EditPanel.IsEnabled = true;
        SaveProfileBtn.IsEnabled = true;
        StatusText.Text = string.Empty;
    }

    private void ClearForm()
    {
        _editing = null;
        DisplayNameBox.Text = string.Empty;
        FirstNameBox.Text   = string.Empty;
        MiddleNameBox.Text  = string.Empty;
        LastNameBox.Text    = string.Empty;
        DobBox.Text         = string.Empty;
        NiBox.Text          = string.Empty;
        EmailBox.Text       = string.Empty;
        PhoneBox.Text       = string.Empty;
        Addr1Box.Text       = string.Empty;
        Addr2Box.Text       = string.Empty;
        CityBox.Text        = string.Empty;
        CountyBox.Text      = string.Empty;
        PostcodeBox.Text    = string.Empty;
        CountryBox.Text     = string.Empty;

        EditPanel.IsEnabled = false;
        SaveProfileBtn.IsEnabled = false;
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var p = new PersonalProfile
        {
            DisplayName = "New Profile",
            Country = "United Kingdom"
        };
        PersonalProfileStore.Add(p);
        ProfileList.SelectedItem = p;
        DisplayNameBox.Focus();
        DisplayNameBox.SelectAll();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;
        bool confirmed = AppDialog.ShowConfirm(
            $"Delete profile \"{_editing.DisplayName}\"?",
            title: "Confirm Delete",
            confirmText: "Delete",
            isDanger: true);
        if (!confirmed) return;

        PersonalProfileStore.Remove(_editing.Id);
        ClearForm();
        StatusText.Text = "Profile deleted.";
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;
        WriteFormToProfile(_editing);
        PersonalProfileStore.Save();
        StatusText.Text = "Saved.";
    }

    private void WriteFormToProfile(PersonalProfile p)
    {
        p.DisplayName              = DisplayNameBox.Text.Trim();
        p.FirstName                = FirstNameBox.Text.Trim();
        p.MiddleName               = MiddleNameBox.Text.Trim();
        p.LastName                 = LastNameBox.Text.Trim();
        p.DateOfBirth              = DobBox.Text.Trim();
        p.NationalInsuranceNumber  = NiBox.Text.Trim();
        p.Email                    = EmailBox.Text.Trim();
        p.Phone                    = PhoneBox.Text.Trim();
        p.AddressLine1             = Addr1Box.Text.Trim();
        p.AddressLine2             = Addr2Box.Text.Trim();
        p.City                     = CityBox.Text.Trim();
        p.County                   = CountyBox.Text.Trim();
        p.Postcode                 = PostcodeBox.Text.Trim();
        p.Country                  = CountryBox.Text.Trim();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}

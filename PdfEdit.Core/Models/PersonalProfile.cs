using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PdfEdit.Models;

public class PersonalProfile : INotifyPropertyChanged
{
    private string _displayName = "My Profile";
    private string _firstName = string.Empty;
    private string _middleName = string.Empty;
    private string _lastName = string.Empty;
    private string _dateOfBirth = string.Empty;
    private string _nationalInsuranceNumber = string.Empty;
    private string _email = string.Empty;
    private string _phone = string.Empty;
    private string _addressLine1 = string.Empty;
    private string _addressLine2 = string.Empty;
    private string _city = string.Empty;
    private string _county = string.Empty;
    private string _postcode = string.Empty;
    private string _country = "United Kingdom";

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    public string FirstName
    {
        get => _firstName;
        set { _firstName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); }
    }

    public string MiddleName
    {
        get => _middleName;
        set { _middleName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); }
    }

    public string LastName
    {
        get => _lastName;
        set { _lastName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullName)); }
    }

    public string DateOfBirth
    {
        get => _dateOfBirth;
        set { _dateOfBirth = value; OnPropertyChanged(); }
    }

    public string NationalInsuranceNumber
    {
        get => _nationalInsuranceNumber;
        set { _nationalInsuranceNumber = value; OnPropertyChanged(); }
    }

    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); }
    }

    public string Phone
    {
        get => _phone;
        set { _phone = value; OnPropertyChanged(); }
    }

    public string AddressLine1
    {
        get => _addressLine1;
        set { _addressLine1 = value; OnPropertyChanged(); }
    }

    public string AddressLine2
    {
        get => _addressLine2;
        set { _addressLine2 = value; OnPropertyChanged(); }
    }

    public string City
    {
        get => _city;
        set { _city = value; OnPropertyChanged(); }
    }

    public string County
    {
        get => _county;
        set { _county = value; OnPropertyChanged(); }
    }

    public string Postcode
    {
        get => _postcode;
        set { _postcode = value; OnPropertyChanged(); }
    }

    public string Country
    {
        get => _country;
        set { _country = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string FullName => string.Join(" ",
        new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public PersonalProfile Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        FirstName = FirstName,
        MiddleName = MiddleName,
        LastName = LastName,
        DateOfBirth = DateOfBirth,
        NationalInsuranceNumber = NationalInsuranceNumber,
        Email = Email,
        Phone = Phone,
        AddressLine1 = AddressLine1,
        AddressLine2 = AddressLine2,
        City = City,
        County = County,
        Postcode = Postcode,
        Country = Country
    };

    public void CopyFrom(PersonalProfile src)
    {
        DisplayName = src.DisplayName;
        FirstName = src.FirstName;
        MiddleName = src.MiddleName;
        LastName = src.LastName;
        DateOfBirth = src.DateOfBirth;
        NationalInsuranceNumber = src.NationalInsuranceNumber;
        Email = src.Email;
        Phone = src.Phone;
        AddressLine1 = src.AddressLine1;
        AddressLine2 = src.AddressLine2;
        City = src.City;
        County = src.County;
        Postcode = src.Postcode;
        Country = src.Country;
    }
}

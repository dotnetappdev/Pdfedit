using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using PdfEdit.Models;

namespace PdfEdit.Services;

public static class PersonalProfileStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PdfEdit", "profiles.json");

    private static ObservableCollection<PersonalProfile>? _cache;

    public static ObservableCollection<PersonalProfile> All
    {
        get
        {
            _cache ??= LoadFromDisk();
            return _cache;
        }
    }

    private static ObservableCollection<PersonalProfile> LoadFromDisk()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<PersonalProfile>>(json);
                if (list != null) return new ObservableCollection<PersonalProfile>(list);
            }
        }
        catch { }
        return new ObservableCollection<PersonalProfile>();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(
                All.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch { }
    }

    public static void Add(PersonalProfile profile)
    {
        All.Add(profile);
        Save();
    }

    public static void Remove(string id)
    {
        var existing = All.FirstOrDefault(p => p.Id == id);
        if (existing != null) All.Remove(existing);
        Save();
    }

    // Smart field-name matching: maps PDF field names to profile values
    public static Dictionary<string, string> MatchFields(
        IEnumerable<string> fieldNames, PersonalProfile profile)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawName in fieldNames)
        {
            var key = Normalize(rawName);
            var value = MatchKey(key, profile);
            if (!string.IsNullOrEmpty(value))
                result[rawName] = value;
        }

        return result;
    }

    private static string Normalize(string name)
        => new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray());

    private static string MatchKey(string key, PersonalProfile p)
    {
        // Full name
        if (Contains(key, "fullname", "wholename", "completename"))
            return p.FullName;

        // First name
        if (Contains(key, "firstname", "forename", "givenname", "fname", "frstname"))
            return p.FirstName;

        // Middle name
        if (Contains(key, "middlename", "middleinitial", "mname"))
            return p.MiddleName;

        // Last name / surname
        if (Contains(key, "lastname", "surname", "familyname", "lname", "lstname"))
            return p.LastName;

        // DOB
        if (Contains(key, "dob", "dateofbirth", "birthdate", "birthday", "bdate", "birthdt"))
            return p.DateOfBirth;

        // NI Number
        if (Contains(key, "ni", "nino", "nationalinsurance", "ninumber", "insurancenumber",
                     "natins", "nicode"))
            return p.NationalInsuranceNumber;

        // Email
        if (Contains(key, "email", "emailaddress", "emailaddr", "mail"))
            return p.Email;

        // Phone
        if (Contains(key, "phone", "telephone", "mobile", "phonenumber", "tel", "cellphone",
                     "mobilenumber", "contact"))
            return p.Phone;

        // Address line 1
        if (Contains(key, "address1", "addressline1", "streetaddress", "street", "addr1",
                     "line1", "houseno", "housenumber", "buildingno"))
            return p.AddressLine1;

        // Address line 2
        if (Contains(key, "address2", "addressline2", "addr2", "line2", "apartment", "flat",
                     "suite", "unit"))
            return p.AddressLine2;

        // City / Town
        if (Contains(key, "city", "town", "locality"))
            return p.City;

        // County / State / Region
        if (Contains(key, "county", "state", "province", "region", "district"))
            return p.County;

        // Postcode / Zip
        if (Contains(key, "postcode", "postalcode", "zipcode", "zip", "postal"))
            return p.Postcode;

        // Country
        if (Contains(key, "country", "nation", "countryofresidence"))
            return p.Country;

        // Bare "name" — use full name
        if (key == "name") return p.FullName;

        // Bare "address" — use address line 1
        if (key == "address") return p.AddressLine1;

        return string.Empty;
    }

    private static bool Contains(string key, params string[] keywords)
        => keywords.Any(k => key.Contains(k, StringComparison.Ordinal));

    public static string BuildSystemPrompt(PersonalProfile profile)
    {
        var lines = new List<string>
        {
            "The user has provided the following personal information to use when filling form fields:",
            $"Full Name: {profile.FullName}",
        };

        if (!string.IsNullOrWhiteSpace(profile.FirstName))
            lines.Add($"First Name: {profile.FirstName}");
        if (!string.IsNullOrWhiteSpace(profile.MiddleName))
            lines.Add($"Middle Name: {profile.MiddleName}");
        if (!string.IsNullOrWhiteSpace(profile.LastName))
            lines.Add($"Last Name: {profile.LastName}");
        if (!string.IsNullOrWhiteSpace(profile.DateOfBirth))
            lines.Add($"Date of Birth: {profile.DateOfBirth}");
        if (!string.IsNullOrWhiteSpace(profile.NationalInsuranceNumber))
            lines.Add($"National Insurance Number: {profile.NationalInsuranceNumber}");
        if (!string.IsNullOrWhiteSpace(profile.Email))
            lines.Add($"Email: {profile.Email}");
        if (!string.IsNullOrWhiteSpace(profile.Phone))
            lines.Add($"Phone: {profile.Phone}");
        if (!string.IsNullOrWhiteSpace(profile.AddressLine1))
            lines.Add($"Address Line 1: {profile.AddressLine1}");
        if (!string.IsNullOrWhiteSpace(profile.AddressLine2))
            lines.Add($"Address Line 2: {profile.AddressLine2}");
        if (!string.IsNullOrWhiteSpace(profile.City))
            lines.Add($"City: {profile.City}");
        if (!string.IsNullOrWhiteSpace(profile.County))
            lines.Add($"County/State: {profile.County}");
        if (!string.IsNullOrWhiteSpace(profile.Postcode))
            lines.Add($"Postcode: {profile.Postcode}");
        if (!string.IsNullOrWhiteSpace(profile.Country))
            lines.Add($"Country: {profile.Country}");

        lines.Add("\nUse this information to fill in matching form fields accurately.");
        return string.Join("\n", lines);
    }
}

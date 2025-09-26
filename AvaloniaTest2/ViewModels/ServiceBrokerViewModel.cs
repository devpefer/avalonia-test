using System;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;

namespace AvaloniaTest2.ViewModels;

public class ServiceBrokerViewModel : ViewModelBase
{
    private const string ConnectionString = "Server=localhost;Database=SBTestDB;User Id=sa;Password=YoMismo69!;TrustServerCertificate=True;";
    private const string QueueName = "TargetQueue";

    private string _status = "Listo";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ObservableCollection<string> Messages { get; } = new();

    public ICommand RefreshCommand { get; }

    public ServiceBrokerViewModel()
    {
        RefreshCommand = new RelayCommand(async () => await LoadMessagesAsync());
    }

    public async Task LoadMessagesAsync()
    {
        Status = "Cargando...";
        try
        {
            await Task.Run(async () =>
            {
                var messages = new ObservableCollection<string>();

                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                string sql = "SELECT CAST(message_body AS NVARCHAR(MAX)) AS message_body FROM [TargetQueue]";

                using var cmd = new SqlCommand(sql, connection);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var body = reader["message_body"];
                    string text = body == DBNull.Value ? "" : (string)body;
                    messages.Add(text);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Messages.Clear();
                    foreach (var msg in messages)
                        Messages.Add(msg);
                });
            });

            Status = "Listo";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }
}

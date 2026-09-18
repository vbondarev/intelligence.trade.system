using System;
using System.Text.Json;
using System.Text.Json.Serialization;

enum Capability { ReadBalance, ReadPositions }

var opts = new JsonSerializerOptions();
opts.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false));
Console.WriteLine(JsonSerializer.Serialize(new Capability[] { (Capability)1, (Capability)2 }, opts));

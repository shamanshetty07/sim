using Newtonsoft.Json.Linq;

namespace Sim.AI.WorldDesign
{
    /// <summary>
    /// The one canonical JSON Schema describing the subset of WorldSpecification an AI world
    /// designer is asked to produce — built once here so a provider that supports native
    /// structured-output/tool-schema enforcement (Anthropic's forced tool use — see
    /// AnthropicLLMClient) has a single authoritative source instead of hand-writing its own
    /// copy. Mirrors LLMWorldDesigner.BuildSystemPrompt()'s field list exactly (same fields, same
    /// nesting, same free-form-vs-enum choices) — the two are necessarily separate
    /// representations (this is a strict machine schema; that is explanatory prose plus a loose
    /// illustrative shape a provider with no schema-enforcement mechanism can still be shown),
    /// but they describe the same contract and must be kept in sync if WorldSpecification's
    /// shape changes. OriginalPrompt/Seed/Metadata are deliberately excluded — the application
    /// always overwrites them after parsing (see WorldSpecificationJsonParser), never trusts the
    /// model for them, so asking the model to produce them would be pointless.
    ///
    /// Deliberately keeps every field the real WorldSpecification model documents as free-form
    /// (TerrainType, EnvironmentObjects[].Category/PlacementHint, Obstacles[].Type,
    /// Course.Style/Difficulty, Weather.Type) as a plain "string", never a JSON Schema `enum` —
    /// constraining these to a fixed value set here would silently reintroduce exactly the
    /// "restrictive keyword-limited architecture" this project has explicitly avoided since
    /// Phase 5 (see each field's own doc-comment on its model class), even though the schema
    /// mechanism makes an enum tempting for reliability. Only WorldScale and FlightStyle are
    /// JSON Schema `enum`s here, because those two — and only those two — really are closed C#
    /// enums on WorldSpecification itself; describing them any other way would be inaccurate,
    /// not more permissive.
    ///
    /// Written to stay within Anthropic's documented structured-output JSON Schema subset
    /// (platform.claude.com/docs/en/build-with-claude/structured-outputs): every object sets
    /// "additionalProperties": false (required for strict mode), no numeric/string length
    /// constraints (minimum/maximum/minLength/maxLength are not supported — range guidance for
    /// 0-1 scores lives in "description" text instead), no recursive `$ref`. Optional fields are
    /// simply left out of "required" rather than modeled as nullable — Newtonsoft's
    /// MissingMemberHandling.Ignore/default-valued properties already make an omitted field a
    /// no-op on the receiving end (see WorldSpecificationJsonParser), so there's no need for a
    /// nullable-type construct the schema subset doesn't reliably support anyway.
    /// </summary>
    public static class WorldSpecificationToolSchema
    {
        public static JObject Build()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["WorldName"] = StringProp("A short, evocative name for this world."),
                    ["Description"] = StringProp("A one- or two-sentence description of the world."),
                    ["Scale"] = EnumProp("Overall world size.", "Small", "Medium", "Large", "Huge"),
                    ["Flight"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["PreferredStyle"] = EnumProp("The kind of FPV flying this world supports.", "Cruise", "Race", "Freestyle", "Technical"),
                            ["TightnessScore01"] = NumberProp("0 = wide open, 1 = dense/technical. Range 0-1."),
                            ["ObstacleDensity01"] = NumberProp("0 = sparse, 1 = dense. Range 0-1."),
                            ["VerticalityScore01"] = NumberProp("0 = flat, 1 = lots of climbing/diving/elevation change. Range 0-1.")
                        }
                    },
                    ["Terrain"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["TerrainType"] = StringProp("Free-form terrain description, e.g. \"mountain\", \"desert\", \"forest\", \"canyon\", \"city\", \"hybrid\"."),
                            ["Width"] = NumberProp("Terrain width in meters."),
                            ["Depth"] = NumberProp("Terrain depth in meters."),
                            ["MaxHeight"] = NumberProp("Maximum terrain height in meters."),
                            ["HeightVariation01"] = NumberProp("0 = flat, 1 = extreme height variation. Range 0-1."),
                            ["HasWater"] = BoolProp("Whether this world has a water feature."),
                            ["WaterFeatureHint"] = StringProp("Free-form water feature description when HasWater is true, e.g. \"waterfalls\", \"river\", \"lake\".")
                        }
                    },
                    ["EnvironmentObjects"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["properties"] = new JObject
                            {
                                ["Category"] = StringProp("Free-form category, e.g. \"tree\", \"rock\", \"building\", \"waterfall\", \"bridge\", \"tunnel\", \"abandoned_building\"."),
                                ["Count"] = IntegerProp("Absolute count, when a specific number is implied."),
                                ["Density01"] = NumberProp("0-1 alternative to Count for area-based placement (e.g. \"dense forest\")."),
                                ["PlacementHint"] = StringProp("Free-form placement hint, e.g. \"along_cliffs\", \"riverbank\", \"scattered\", \"dense_cluster\".")
                            }
                        }
                    },
                    ["Obstacles"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["properties"] = new JObject
                            {
                                ["Id"] = StringProp("Stable identifier, e.g. \"gate_01\"."),
                                ["Type"] = StringProp("Free-form obstacle type — common values: \"gate\", \"ring\", \"wall\", \"pole\", \"tunnel\", \"checkpoint\", \"landing_pad\", but any descriptive value is fine."),
                                ["Position"] = Vector3Prop(),
                                ["RotationEuler"] = Vector3Prop(),
                                ["Scale"] = Vector3Prop(),
                                ["CheckpointIndex"] = IntegerProp("Position in the checkpoint sequence, if part of a race course. Omit if purely decorative.")
                            }
                        }
                    },
                    ["Course"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["Style"] = StringProp("Free-form course style, e.g. \"technical_then_high_speed\", \"circuit\", \"point_to_point\", \"freestyle_park\"."),
                            ["Difficulty"] = StringProp("Free-form difficulty, e.g. \"easy\", \"medium\", \"hard\", \"expert\"."),
                            ["GateCount"] = IntegerProp("The intended number of racing gates."),
                            ["SectionDescriptions"] = new JObject
                            {
                                ["type"] = "array",
                                ["items"] = new JObject { ["type"] = "string" },
                                ["description"] = "Ordered, free-form description of each section of the course, e.g. [\"technical and tight\", \"opens into a high-speed valley\"]."
                            }
                        }
                    },
                    ["Weather"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["Type"] = StringProp("Free-form weather, e.g. \"clear\", \"cloudy\", \"rain\", \"fog\", \"wind\"."),
                            ["FogDensity01"] = NumberProp("Range 0-1."),
                            ["WindStrength01"] = NumberProp("Range 0-1.")
                        }
                    },
                    ["Lighting"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["TimeOfDayHours"] = NumberProp("0-24. Fractional hours are fine (e.g. 16.5 = 4:30pm)."),
                            ["SunIntensity"] = NumberProp("Typical range roughly 0.5-2.")
                        }
                    },
                    ["Spawn"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["Position"] = Vector3Prop()
                        }
                    }
                }
            };
        }

        private static JObject Vector3Prop() => new JObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JObject
            {
                ["x"] = NumberProp(null),
                ["y"] = NumberProp(null),
                ["z"] = NumberProp(null)
            }
        };

        private static JObject StringProp(string description)
        {
            var prop = new JObject { ["type"] = "string" };
            if (!string.IsNullOrEmpty(description)) prop["description"] = description;
            return prop;
        }

        private static JObject NumberProp(string description)
        {
            var prop = new JObject { ["type"] = "number" };
            if (!string.IsNullOrEmpty(description)) prop["description"] = description;
            return prop;
        }

        private static JObject IntegerProp(string description)
        {
            var prop = new JObject { ["type"] = "integer" };
            if (!string.IsNullOrEmpty(description)) prop["description"] = description;
            return prop;
        }

        private static JObject BoolProp(string description)
        {
            var prop = new JObject { ["type"] = "boolean" };
            if (!string.IsNullOrEmpty(description)) prop["description"] = description;
            return prop;
        }

        private static JObject EnumProp(string description, params string[] values)
        {
            var prop = new JObject
            {
                ["type"] = "string",
                ["description"] = description,
                ["enum"] = new JArray(values)
            };
            return prop;
        }
    }
}

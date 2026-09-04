using System.Collections.Generic;

namespace Sim.WorldGeneration.Models
{
    /// <summary>
    /// Race/gameplay-course intent — added Phase 7 specifically so the AI World Designer can
    /// communicate *intent*, not just object counts. A prompt like "15 gates, make the first
    /// section technical and tight, then open into a high-speed valley" carries information no
    /// amount of individual ObstacleSpecification entries expresses on their own: an overall
    /// style, a difficulty, and an ordered narrative across sections of the course.
    ///
    /// Distinct from FlightCharacteristics (which stays as normalized 0-1 scores for generator
    /// tuning): this is the human-readable "what did the user actually ask for" record, and the
    /// place a future multi-section generator (Phase 9+) would read a per-section narrative
    /// from. SectionDescriptions is deliberately free-form text for now rather than a fully
    /// structured per-section model (bounds, terrain-per-section, etc.) — that structure isn't
    /// needed until a generator exists to consume it, and premature structure here would be
    /// exactly the kind of restrictive modeling this project has been avoiding since Phase 5.
    /// </summary>
    public sealed class CourseSpecification
    {
        /// <summary>Free-form: "technical_then_high_speed", "circuit", "point_to_point", "freestyle_park", etc.</summary>
        public string Style { get; set; } = "freestyle";

        /// <summary>Free-form: "easy", "medium", "hard", "expert".</summary>
        public string Difficulty { get; set; } = "medium";

        /// <summary>
        /// The user's *intended* gate count — independent of how many Obstacles entries of
        /// type "gate" actually end up in the specification (those may include rings, walls,
        /// etc. alongside gates, or a generator may not yet have placed all of them). This is
        /// the number to validate/report against, not a derived count.
        /// </summary>
        public int GateCount { get; set; }

        /// <summary>
        /// Ordered, free-form description of each section of the course, e.g.
        /// ["technical and tight", "opens into a high-speed valley"]. Empty if the prompt
        /// didn't describe a multi-section narrative.
        /// </summary>
        public List<string> SectionDescriptions { get; set; } = new List<string>();
    }
}

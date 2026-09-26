Feature: Practical Upgrades integration
  The mod's definitions, configuration, information entries, and runtime patches
  remain coherent when loaded by the real RimWorld engine.

  Scenario: Common upgrade content loads
    Then the practical upgrade definitions are loaded
    And the tool cabinet upgrade levels match the design
    And the practical upgrade Harmony patches are active

  Scenario: Odyssey content is correctly integrated
    Then Odyssey definitions and grav engine upgrades are correctly gated
    And the grav engine upgrade levels match the design
    And grav engine module recipes are attached to vanilla gravtech
    And grav energy fuel calculations are correct
    And the grav engine information entries are complete

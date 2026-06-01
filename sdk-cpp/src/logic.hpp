#pragma once

#include <memory>
#include <optional>
#include <string>

#include "agent.hpp"

auto createAgent(std::string token, std::string serverUrl,
                 std::optional<std::string> playerName = std::nullopt)
    -> std::unique_ptr<thuai::Agent>;

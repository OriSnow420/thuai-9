#include "agent.hpp"
#include <cstdlib>

class MyAgent : public thuai::Agent {
public:
    using Agent::Agent;

    void onGameState(const thuai::GameState& state) override {
        std::cout << "Game: " << state.stage
                  << " Day=" << state.currentDay
                  << " Tick=" << state.currentTick << std::endl;
    }

    void onMarketState(const thuai::MarketState& state) override {
        // Implement your trading logic here
    }

    void onPlayerState(const thuai::PlayerState& state) override {
        // Check your portfolio here
    }

    void onNews(const thuai::News& news) override {
        std::cout << "News [" << news.newsId << "]: " << news.content << std::endl;
        // Example: submit a research report predicting Long
        submitReport(news.newsId, thuai::Prediction::Long);
    }

    void onStrategyOptions(const thuai::StrategyOptions& options) override {
        // Pick the first available card
        if (options.infrastructure) selectStrategy(options.infrastructure->name);
        else if (options.riskControl) selectStrategy(options.riskControl->name);
        else if (options.finTech) selectStrategy(options.finTech->name);
    }
};

int main() {
    const char* token = std::getenv("TOKEN");
    const char* server = std::getenv("SERVER");

    MyAgent agent(
        token ? token : "player1",
        server ? server : "ws://localhost:14514"
    );
    agent.run();
    return 0;
}

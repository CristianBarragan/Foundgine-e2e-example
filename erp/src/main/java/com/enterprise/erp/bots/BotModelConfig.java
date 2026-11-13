package com.enterprise.erp.bots;

import dev.langchain4j.model.chat.ChatModel;
import dev.langchain4j.model.openai.OpenAiChatModel;
import java.time.Duration;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * Chat model for bots.mode=llm: any OpenAI-compatible endpoint (OpenAI, Azure OpenAI gateway, Ollama, LiteLLM...).
 * Not created in the default rules mode, so no key is needed to run or test the ERP.
 */
@Configuration
@ConditionalOnProperty(name = "bots.mode", havingValue = "llm")
class BotModelConfig {
    @Bean
    ChatModel fulfilmentChatModel(@Value("${bots.llm.base-url}") String baseUrl,
                                  @Value("${bots.llm.api-key}") String apiKey,
                                  @Value("${bots.llm.model}") String model) {
        return OpenAiChatModel.builder().baseUrl(baseUrl).apiKey(apiKey).modelName(model)
                .temperature(0.0).timeout(Duration.ofSeconds(90)).build();
    }
}

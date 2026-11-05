# Documento de Requisitos — Loja Virtual (MVP)

Este documento descreve o escopo do MVP da loja virtual. O preâmbulo antes do primeiro Epic é ignorado
pelo parser.

# Epic: Gestão de Pedidos Online
Permitir que clientes comprem pelo site sem atendimento humano, reduzindo o custo por pedido.
Tags: mvp, ecommerce
Prioridade: Alta
Valor de negócio: 80

## Feature: Carrinho de compras
Cliente monta o pedido antes de pagar.

### User Story: Adicionar item ao carrinho (5 SP)
Como cliente,
quero adicionar produtos ao carrinho,
para que eu possa comprá-los em um único pedido.

**Critérios de aceite:**
- Dado um produto com estoque, quando clico em "Adicionar", então o item aparece no carrinho com quantidade 1
- Dado um produto sem estoque, quando tento adicioná-lo, então vejo a mensagem "Produto indisponível"

- Task: Criar endpoint POST /api/cart/items (6h)
- Task: Criar componente de carrinho no frontend (1 dia)
- Task: Testes automatizados do carrinho — 4h

### User Story: Remover item do carrinho
Como cliente, quero remover itens do carrinho, para que eu pague apenas pelo que desejo.

**Estimativas:**
- Story Points: 3
- Pontos de função: 4

Critérios de aceite:
Cenário: remover o último item
Dado um carrinho com um único item
Quando removo o item
Então o carrinho exibe "Seu carrinho está vazio"

- Task: Criar endpoint DELETE /api/cart/items/{id} (3h)

#### Bug: Total do carrinho não atualiza ao remover item (2h)
Passos para reproduzir:
1. Adicionar dois produtos ao carrinho
2. Remover um deles
Resultado esperado: o total é recalculado
Resultado atual: o total continua somando o item removido

## Feature: Checkout e pagamento

### US 2.1: Pagar com cartão de crédito (8 SP)
Como cliente, quero pagar com cartão de crédito, para que eu conclua a compra na hora.

Critérios de aceite:
- Dado um cartão válido, quando confirmo o pagamento, então o pedido muda para "Pago"
- Dado um cartão recusado, quando confirmo, então vejo o motivo da recusa e posso tentar outro cartão

- Task: Integrar gateway de pagamento (sandbox) (12h)
- Task: Tela de pagamento (8h)
- Task: Testes de integração com o gateway (4h)

export const exibidorImagem = (elemento, link) => {
    elemento.src = link == null ? '/default-championship-image.png' : link
}
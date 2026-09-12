import { Locale, Messages } from 'i18n';

/**
 * Storefront strings, English and Bangla.
 *
 * Every buyer-facing string lives here — including error text, empty states and button labels,
 * which are the ones usually forgotten and the ones a confused shopper reads most carefully.
 *
 * Bangla notes:
 *  - Prices render with Bangla digits via Intl, so the taka sign is kept separate from the number.
 *  - "শপ" is used rather than "দোকান" for a shop on the platform, matching how online sellers in
 *    Dhaka describe themselves; "দোকান" reads as a physical stall.
 */
const en: Messages = {
  // Chrome
  'brand.name': 'Lifestyle',
  'nav.searchPlaceholder': 'Search everything on Lifestyle',
  'nav.searchLabel': 'Search everything on Lifestyle',
  'nav.search': 'Search',
  'nav.clearSearch': 'Clear search',
  'nav.home': 'Home',
  'nav.shops': 'Shops',
  'nav.sell': 'Sell',
  'nav.browse': 'Browse',
  'nav.sellOnLifestyle': 'Sell on Lifestyle',
  'nav.language': 'Language',

  'footer.note':
    'A marketplace for Bangladeshi shops. Ordering happens over WhatsApp with the seller — no checkout in this version.',

  // Home
  'home.heroKicker': 'UP TO 25% OFF',
  'home.heroTitle': 'The Festive Edit',
  'home.heroSub': 'Shops across Bangladesh, one place. Order straight from the seller on WhatsApp.',
  'home.all': 'All',
  'home.freshThisWeek': 'Fresh this week',
  'home.resultsFor': 'Results for “{term}”',
  'home.itemCount': '{count} items',
  'home.itemCountOne': '1 item',
  'home.clear': 'Clear',
  'home.sortBy': 'Sort by',
  'home.sortNewest': 'Newest',
  'home.sortPriceAsc': 'Price: low to high',
  'home.sortPriceDesc': 'Price: high to low',
  'home.sortName': 'Name',
  'home.loadMore': 'Load more',
  'home.showingOf': 'Showing {shown} of {total}',
  'home.nothingMatched': 'Nothing matched.',
  'home.noResultsFor': 'No products found for “{term}”. Try a shorter word, or browse a category.',
  'home.nothingInCategory': 'Nothing published in this category yet.',
  'home.browseEverything': 'Browse everything',
  'home.loadFailed': 'Could not reach the catalogue just now. Please refresh.',

  // Product cards and stock
  'stock.inStock': 'In stock',
  'stock.onlyAFewLeft': 'Only a few left',
  'stock.outOfStock': 'Out of stock',
  'price.was': 'Was {amount}',
  'price.save': 'SAVE {percent}%',

  // Product page
  'product.notFound': 'Product not found',
  'product.notFoundBody': 'It may have been unpublished, or the link is wrong.',
  'product.backToShopping': 'Back to shopping',
  'product.orderOnWhatsApp': 'Order on WhatsApp',
  'product.noWhatsApp':
    'This shop has not added a WhatsApp number yet — quote the reference below when you contact them.',
  'product.reference': 'REF {code}',
  'product.copy': 'Copy',
  'product.copied': 'Copied',
  'product.verifiedSeller': 'Verified seller',
  'product.onLifestyleSince': 'On Lifestyle since {year}',
  'product.about': 'About this product',
  'product.details': 'Details',
  'product.imageNumber': 'Image {n}',
  'product.whatsAppGreeting': 'Hi {shop}, I would like to order:',
  'product.whatsAppPrice': 'Price: {price}',
  'product.whatsAppRef': 'Ref: {code}',

  // Shops
  'shops.title': 'Shops',
  'shops.subtitle': 'Every shop on Lifestyle. Each one runs its own storefront.',
  'shops.loadFailed': 'Could not load the shop directory just now. Please refresh.',
  'shops.none': 'No shops are open yet.',
  'shop.notFound': 'Shop not found',
  'shop.notFoundBody': 'It may have closed, or the link is wrong.',
  'shop.allShops': 'All shops',
  'shop.productCount': '{count} products',
  'shop.productCountOne': '1 product',
  'shop.nothingPublished': 'This shop has not published anything yet.',
};

const bn: Messages = {
  'brand.name': 'লাইফস্টাইল',
  'nav.searchPlaceholder': 'লাইফস্টাইলে সবকিছু খুঁজুন',
  'nav.searchLabel': 'লাইফস্টাইলে সবকিছু খুঁজুন',
  'nav.search': 'খুঁজুন',
  'nav.clearSearch': 'সার্চ মুছুন',
  'nav.home': 'হোম',
  'nav.shops': 'শপ',
  'nav.sell': 'বিক্রি',
  'nav.browse': 'ব্রাউজ',
  'nav.sellOnLifestyle': 'লাইফস্টাইলে বিক্রি করুন',
  'nav.language': 'ভাষা',

  'footer.note':
    'বাংলাদেশের শপগুলোর জন্য একটি মার্কেটপ্লেস। অর্ডার হয় সরাসরি বিক্রেতার সাথে হোয়াটসঅ্যাপে — এই সংস্করণে কোনো চেকআউট নেই।',

  'home.heroKicker': '২৫% পর্যন্ত ছাড়',
  'home.heroTitle': 'উৎসব সংগ্রহ',
  'home.heroSub': 'সারা বাংলাদেশের শপ, এক জায়গায়। সরাসরি বিক্রেতার কাছ থেকে হোয়াটসঅ্যাপে অর্ডার করুন।',
  'home.all': 'সব',
  'home.freshThisWeek': 'এই সপ্তাহের নতুন',
  'home.resultsFor': '“{term}” এর ফলাফল',
  'home.itemCount': '{count}টি পণ্য',
  'home.itemCountOne': '১টি পণ্য',
  'home.clear': 'মুছুন',
  'home.sortBy': 'সাজান',
  'home.sortNewest': 'নতুন আগে',
  'home.sortPriceAsc': 'দাম: কম থেকে বেশি',
  'home.sortPriceDesc': 'দাম: বেশি থেকে কম',
  'home.sortName': 'নাম',
  'home.loadMore': 'আরও দেখুন',
  'home.showingOf': '{total}টির মধ্যে {shown}টি দেখানো হচ্ছে',
  'home.nothingMatched': 'কিছু পাওয়া যায়নি।',
  'home.noResultsFor': '“{term}” এর জন্য কোনো পণ্য পাওয়া যায়নি। ছোট শব্দ দিয়ে চেষ্টা করুন, বা কোনো ক্যাটাগরি দেখুন।',
  'home.nothingInCategory': 'এই ক্যাটাগরিতে এখনো কিছু প্রকাশ করা হয়নি।',
  'home.browseEverything': 'সব দেখুন',
  'home.loadFailed': 'এই মুহূর্তে ক্যাটালগে পৌঁছানো যাচ্ছে না। রিফ্রেশ করুন।',

  'stock.inStock': 'স্টকে আছে',
  'stock.onlyAFewLeft': 'অল্প কিছু বাকি',
  'stock.outOfStock': 'স্টকে নেই',
  'price.was': 'আগে {amount}',
  'price.save': '{percent}% ছাড়',

  'product.notFound': 'পণ্যটি পাওয়া যায়নি',
  'product.notFoundBody': 'এটি হয়তো সরিয়ে নেওয়া হয়েছে, অথবা লিংকটি ভুল।',
  'product.backToShopping': 'কেনাকাটায় ফিরুন',
  'product.orderOnWhatsApp': 'হোয়াটসঅ্যাপে অর্ডার করুন',
  'product.noWhatsApp':
    'এই শপ এখনো হোয়াটসঅ্যাপ নম্বর যোগ করেনি — যোগাযোগ করার সময় নিচের রেফারেন্সটি জানান।',
  'product.reference': 'রেফ {code}',
  'product.copy': 'কপি',
  'product.copied': 'কপি হয়েছে',
  'product.verifiedSeller': 'যাচাইকৃত বিক্রেতা',
  'product.onLifestyleSince': 'লাইফস্টাইলে {year} সাল থেকে',
  'product.about': 'পণ্য সম্পর্কে',
  'product.details': 'বিস্তারিত',
  'product.imageNumber': 'ছবি {n}',
  'product.whatsAppGreeting': '{shop}, আমি অর্ডার করতে চাই:',
  'product.whatsAppPrice': 'দাম: {price}',
  'product.whatsAppRef': 'রেফ: {code}',

  'shops.title': 'শপসমূহ',
  'shops.subtitle': 'লাইফস্টাইলের সব শপ। প্রত্যেকটির নিজস্ব স্টোরফ্রন্ট আছে।',
  'shops.loadFailed': 'এই মুহূর্তে শপের তালিকা আনা যাচ্ছে না। রিফ্রেশ করুন।',
  'shops.none': 'এখনো কোনো শপ খোলা হয়নি।',
  'shop.notFound': 'শপটি পাওয়া যায়নি',
  'shop.notFoundBody': 'এটি হয়তো বন্ধ হয়ে গেছে, অথবা লিংকটি ভুল।',
  'shop.allShops': 'সব শপ',
  'shop.productCount': '{count}টি পণ্য',
  'shop.productCountOne': '১টি পণ্য',
  'shop.nothingPublished': 'এই শপ এখনো কিছু প্রকাশ করেনি।',
};

export const STOREFRONT_MESSAGES: Record<Locale, Messages> = { en, bn };
